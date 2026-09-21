using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Joblink.Tests.Support;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // Two real users, the real API and the real Joblinkv2 database: the checks that
    // the ownership rules really live in the SQL. Each test makes its own users
    // (and skills) and deletes everything it made.
    public sealed class ResumeFamilyDbTests : IClassFixture<DbApiFactory>, IDisposable
    {
        private readonly DbApiFactory _factory;
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();

        public ResumeFamilyDbTests(DbApiFactory factory) => _factory = factory;

        // ----- helpers ----------------------------------------------------------

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private int NewUser(string role = "user")
        {
            using var db = Open();

            var id = db.QuerySingle<int>(
                @"INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted)
                  VALUES (@name, @email, 'x', @role, GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest {_tag}", email = $"dbtest.{_tag}.{_userIds.Count}@example.com", role });

            _userIds.Add(id);

            return id;
        }

        private int NewSkill(string name)
        {
            using var db = Open();

            return db.QuerySingle<int>(
                "INSERT INTO Skills (skill_name, is_deleted) VALUES (@name, 0); SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest-{_tag}-{name}" });
        }

        private HttpClient As(int userId, string role = "user") => _factory.ClientFor(userId, role);

        private static Task<HttpResponseMessage> Call(HttpClient client, string method, string url, string? json = null) =>
            client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
            });

        private static async Task<JsonElement> Json(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private T Value<T>(string sql, object? args = null)
        {
            using var db = Open();
            return db.QuerySingle<T>(sql, args);
        }

        private int Count(string sql, object? args = null) => Value<int>(sql, args);

        private string? ChangedText(string table, string idColumn, int entryId, string column) =>
            Value<string?>($"SELECT {column} FROM {table} WHERE {idColumn} = @entryId", new { entryId });

        private async Task<int> CreateResume(int userId, string title = "My Resume")
        {
            var response = await Call(As(userId), "POST", "/api/Resume", $"{{\"title\":\"{title}\"}}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Json(response)).GetProperty("resumeId").GetInt32();
        }

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Resume_Skills WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Education     WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Experience    WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Resumes WHERE user_id IN @users;
                DELETE FROM Profiles WHERE user_id IN @users;
                DELETE FROM Job_Preferences WHERE user_id IN @users;
                DELETE FROM Skills WHERE skill_name LIKE @skills;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users, skills = $"dbtest-{_tag}-%" });
        }

        // ----- profile ----------------------------------------------------------

        [DbFact]
        public async Task A_profile_belongs_to_the_caller_whatever_the_body_says()
        {
            var a = NewUser();
            var b = NewUser();

            var created = await Call(As(a), "POST", "/api/Profile",
                $$"""{"phone":"0917","address":"Makati","linkedinUrl":"https://www.linkedin.com/in/a","userId":{{b}},"isDeleted":true,"profileId":9999}""");
            var body = await Json(created);

            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Equal(a, body.GetProperty("userId").GetInt32());
            Assert.NotEqual(9999, body.GetProperty("profileId").GetInt32());
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Profiles WHERE user_id = @a AND is_deleted = 0", new { a }));
            Assert.Equal(0, Count("SELECT COUNT(*) FROM Profiles WHERE user_id = @b", new { b }));
        }

        [DbFact]
        public async Task You_get_one_profile_a_second_is_a_409()
        {
            var a = NewUser();

            await Call(As(a), "POST", "/api/Profile", """{"phone":"1"}""");
            var second = await Call(As(a), "POST", "/api/Profile", """{"phone":"2"}""");

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("profile_exists", (await Json(second)).GetProperty("code").GetString());
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Profiles WHERE user_id = @a", new { a }));
            Assert.Equal("1", Value<string>("SELECT phone FROM Profiles WHERE user_id = @a", new { a }));
        }

        [DbFact]
        public async Task Nobody_else_can_read_change_or_delete_your_profile()
        {
            var a = NewUser();
            var b = NewUser();

            var profileId = (await Json(await Call(As(a), "POST", "/api/Profile", """{"phone":"0917","address":"Makati"}"""))).GetProperty("profileId").GetInt32();

            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "GET", $"/api/Profile/{profileId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Call(As(b), "GET", $"/api/Profile/by-user/{a}")).StatusCode);

            // B has no profile yet: naming A's profile changes nothing
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "PUT", "/api/Profile", $$"""{"profileId":{{profileId}},"phone":"HACKED"}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "DELETE", $"/api/Profile?id={profileId}")).StatusCode);

            // ...and once B has one of their own, still nothing happens to A's
            await Call(As(b), "POST", "/api/Profile", """{"phone":"5555"}""");

            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "PUT", "/api/Profile", $$"""{"profileId":{{profileId}},"phone":"HACKED"}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "DELETE", $"/api/Profile?id={profileId}")).StatusCode);

            Assert.Equal("0917", Value<string>("SELECT phone FROM Profiles WHERE profile_id = @profileId", new { profileId }));
            Assert.Equal(0, Count("SELECT CAST(is_deleted AS int) FROM Profiles WHERE profile_id = @profileId", new { profileId }));
            Assert.Equal("5555", Value<string>("SELECT phone FROM Profiles WHERE user_id = @b", new { b }));
        }

        [DbFact]
        public async Task Updating_replaces_your_details_and_ignores_owner_and_deleted_flag()
        {
            var a = NewUser();
            var b = NewUser();

            var profileId = (await Json(await Call(As(a), "POST", "/api/Profile", """{"phone":"0917"}"""))).GetProperty("profileId").GetInt32();

            var update = await Call(As(a), "PUT", "/api/Profile",
                $$"""{"profileId":{{profileId}},"phone":"0999","address":"Taguig","userId":{{b}},"isDeleted":true}""");

            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var row = Value<(int UserId, string Phone, string Address, bool Deleted)>(
                "SELECT user_id AS UserId, phone AS Phone, CAST(address AS varchar(100)) AS Address, CAST(is_deleted AS bit) AS Deleted FROM Profiles WHERE profile_id = @profileId", new { profileId });

            Assert.Equal((a, "0999", "Taguig", false), row);
            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "GET", $"/api/Profile/by-user/{a}")).StatusCode);

            // and you can delete it, after which it is gone
            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "DELETE", $"/api/Profile?id={profileId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "GET", $"/api/Profile/by-user/{a}")).StatusCode);
        }

        [DbFact]
        public async Task An_employer_has_a_profile_too_and_it_is_just_as_private()
        {
            var boss = NewUser("employer");
            var seeker = NewUser();

            var created = await Call(As(boss, "employer"), "POST", "/api/Profile", """{"phone":"0288"}""");
            var profileId = (await Json(created)).GetProperty("profileId").GetInt32();

            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(seeker), "GET", $"/api/Profile/{profileId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Call(As(seeker), "GET", $"/api/Profile/by-user/{boss}")).StatusCode);
        }

        // ----- resumes ----------------------------------------------------------

        [DbFact]
        public async Task A_new_resume_belongs_to_the_caller_and_is_stamped_by_the_server()
        {
            var a = NewUser();
            var b = NewUser();

            var response = await Call(As(a), "POST", "/api/Resume",
                $$"""{"title":"Dev CV","templateType":"modern","userId":{{b}},"isDeleted":true,"createdAt":"2000-01-01T00:00:00","resumeId":1}""");
            var body = await Json(response);
            var id = body.GetProperty("resumeId").GetInt32();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(1, id);

            var row = Value<(int UserId, string Title, string Template, bool Deleted, DateTime CreatedAt)>(
                "SELECT user_id AS UserId, title AS Title, template_type AS Template, CAST(is_deleted AS bit) AS Deleted, created_at AS CreatedAt FROM Resumes WHERE resume_id = @id", new { id });

            Assert.Equal(a, row.UserId);
            Assert.Equal(("Dev CV", "modern", false), (row.Title, row.Template, row.Deleted));
            // stamped by the server (the test app's clock), not the 2000-01-01 the body asked for
            Assert.Equal(_factory.Clock.GetLocalNow().DateTime, row.CreatedAt);
        }

        [DbFact]
        public async Task A_bare_create_gets_the_defaults_the_builder_relies_on()
        {
            var a = NewUser();

            var response = await Call(As(a), "POST", "/api/Resume", "{}");
            var body = await Json(response);

            Assert.Equal("My Resume", body.GetProperty("title").GetString());
            Assert.Equal("standard", body.GetProperty("templateType").GetString());
        }

        [DbFact]
        public async Task Nobody_else_can_read_change_or_delete_your_resume()
        {
            var a = NewUser();
            var b = NewUser();
            var resumeId = await CreateResume(a, "Private");

            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "GET", $"/api/Resume/{resumeId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Call(As(b), "GET", $"/api/Resume/by-user/{a}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "PUT", "/api/Resume", $$"""{"resumeId":{{resumeId}},"title":"HACKED","aiGeneratedContent":"HACKED"}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "DELETE", $"/api/Resume?id={resumeId}")).StatusCode);

            Assert.Equal("Private", Value<string>("SELECT title FROM Resumes WHERE resume_id = @resumeId", new { resumeId }));
            Assert.Equal(0, Count("SELECT CAST(is_deleted AS int) FROM Resumes WHERE resume_id = @resumeId", new { resumeId }));

            var bList = await Json(await Call(As(b), "GET", $"/api/Resume/by-user/{b}"));

            Assert.Equal(0, bList.GetArrayLength());
        }

        [DbFact]
        public async Task Saving_a_resume_changes_only_its_title_and_content()
        {
            var a = NewUser();
            var b = NewUser();
            var resumeId = await CreateResume(a, "Old");

            var response = await Call(As(a), "PUT", "/api/Resume",
                $$"""{"resumeId":{{resumeId}},"title":"New","aiGeneratedContent":"A summary","userId":{{b}},"isDeleted":true}""");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var row = Value<(int UserId, string Title, string Content, string Template, bool Deleted)>(
                "SELECT user_id AS UserId, title AS Title, CAST(ai_generated_content AS varchar(100)) AS Content, template_type AS Template, CAST(is_deleted AS bit) AS Deleted FROM Resumes WHERE resume_id = @resumeId", new { resumeId });

            Assert.Equal((a, "New", "A summary", "standard", false), row);
        }

        [DbFact]
        public async Task Deleting_a_resume_deletes_that_resume_and_nothing_else()
        {
            // The old delete treated the id as a USER id and removed all of that user's rows.
            var a = NewUser();
            var first = await CreateResume(a, "One");
            var second = await CreateResume(a, "Two");

            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "DELETE", $"/api/Resume?id={first}")).StatusCode);

            var list = await Json(await Call(As(a), "GET", $"/api/Resume/by-user/{a}"));

            Assert.Equal(new[] { second }, list.EnumerateArray().Select(r => r.GetProperty("resumeId").GetInt32()).ToArray());
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "DELETE", $"/api/Resume?id={first}")).StatusCode);
        }

        // ----- education and experience: the same rules, so one scenario for both -----

        private async Task EntriesBelongToYourOwnResumeOnly(string route, string idKey, string table, string idColumn, string fields, string changedFields, string changedColumn)
        {
            var a = NewUser();
            var b = NewUser();
            var resumeA = await CreateResume(a);
            var resumeB = await CreateResume(b);

            // create: owner comes from the resume; ids and flags in the body are ignored
            var created = await Call(As(a), "POST", $"/api/{route}", $$"""{"resumeId":{{resumeA}},"{{idKey}}":999999,"isDeleted":true,{{fields}}}""");
            var entryId = (await Json(created)).GetProperty(idKey).GetInt32();

            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.NotEqual(999999, entryId);
            Assert.Equal(resumeA, Value<int>($"SELECT resume_id FROM {table} WHERE {idColumn} = @entryId", new { entryId }));
            Assert.Equal(0, Count($"SELECT CAST(is_deleted AS int) FROM {table} WHERE {idColumn} = @entryId", new { entryId }));

            // B can't see it, list it, change it, delete it, or add to A's resume
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "GET", $"/api/{route}/{entryId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "GET", $"/api/{route}/by-resume/{resumeA}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "POST", $"/api/{route}", $$"""{"resumeId":{{resumeA}},{{fields}}}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "PUT", $"/api/{route}", $$"""{"{{idKey}}":{{entryId}},{{changedFields}}}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "DELETE", $"/api/{route}?id={entryId}")).StatusCode);
            Assert.Equal(1, Count($"SELECT COUNT(*) FROM {table} WHERE resume_id = @resumeA", new { resumeA }));
            Assert.NotEqual("CHANGED", ChangedText(table, idColumn, entryId, changedColumn));

            // A can save it - and can't move it to B's resume by naming it in the body.
            // Month-picker dates ("2021-05") are accepted.
            var saved = await Call(As(a), "PUT", $"/api/{route}",
                $$"""{"{{idKey}}":{{entryId}},"resumeId":{{resumeB}},"isDeleted":true,"startDate":"2021-05","endDate":null,{{changedFields}}}""");

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(resumeA, Value<int>($"SELECT resume_id FROM {table} WHERE {idColumn} = @entryId", new { entryId }));
            Assert.Equal(0, Count($"SELECT CAST(is_deleted AS int) FROM {table} WHERE {idColumn} = @entryId", new { entryId }));
            Assert.Equal(new DateTime(2021, 5, 1), Value<DateTime>($"SELECT start_date FROM {table} WHERE {idColumn} = @entryId", new { entryId }));
            Assert.Equal("CHANGED", ChangedText(table, idColumn, entryId, changedColumn));
            Assert.Equal(0, Count($"SELECT COUNT(*) FROM {table} WHERE resume_id = @resumeB", new { resumeB }));

            // A's list has it; deleting it works (this returned a 500 before) and removes it
            var list = await Json(await Call(As(a), "GET", $"/api/{route}/by-resume/{resumeA}"));

            Assert.Equal(new[] { entryId }, list.EnumerateArray().Select(e => e.GetProperty(idKey).GetInt32()).ToArray());
            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "DELETE", $"/api/{route}?id={entryId}")).StatusCode);
            Assert.Equal(0, (await Json(await Call(As(a), "GET", $"/api/{route}/by-resume/{resumeA}"))).GetArrayLength());
            Assert.Equal(1, Count($"SELECT CAST(is_deleted AS int) FROM {table} WHERE {idColumn} = @entryId", new { entryId }));
        }

        [DbFact]
        public Task Education_entries_belong_to_your_own_resume_only() =>
            EntriesBelongToYourOwnResumeOnly("Education", "educationId", "Education", "education_id",
                "\"schoolName\":\"UP Diliman\",\"degree\":\"BS CS\"", "\"schoolName\":\"CHANGED\",\"degree\":\"MS\"", "school_name");

        [DbFact]
        public Task Experience_entries_belong_to_your_own_resume_only() =>
            EntriesBelongToYourOwnResumeOnly("Experience", "experienceId", "Experience", "experience_id",
                "\"companyName\":\"Acme\",\"position\":\"Dev\",\"description\":\"Built things\"", "\"companyName\":\"Globex\",\"position\":\"CHANGED\",\"description\":\"More\"", "position");

        [DbFact]
        public async Task Long_and_awkward_text_is_stored_exactly_as_typed()
        {
            var a = NewUser();
            var resumeId = await CreateResume(a);
            var description = "It's \"quoted\", 100% <b>bold</b>; DROP TABLE Experience; -- " + new string('x', 15000);

            var created = await Call(As(a), "POST", "/api/Experience",
                JsonSerializer.Serialize(new { resumeId, position = "Dev", description }));
            var id = (await Json(created)).GetProperty("experienceId").GetInt32();

            var read = await Json(await Call(As(a), "GET", $"/api/Experience/{id}"));

            Assert.Equal(description, read.GetProperty("description").GetString());
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Experience WHERE experience_id = @id", new { id }));
        }

        // ----- skills on a resume ----------------------------------------------

        [DbFact]
        public async Task A_skill_can_be_added_removed_and_added_back()
        {
            var a = NewUser();
            var resumeId = await CreateResume(a);
            var skillId = NewSkill("csharp");

            var add = () => Call(As(a), "POST", "/api/ResumeSkills", $$"""{"resumeId":{{resumeId}},"skillId":{{skillId}},"isDeleted":true}""");

            Assert.Equal(HttpStatusCode.OK, (await add()).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await add()).StatusCode);    // twice is fine
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Resume_Skills WHERE resume_id = @resumeId AND skill_id = @skillId AND is_deleted = 0", new { resumeId, skillId }));

            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "DELETE", $"/api/ResumeSkills/{resumeId}/{skillId}")).StatusCode);
            Assert.Equal(0, (await Json(await Call(As(a), "GET", $"/api/ResumeSkills/by-resume/{resumeId}"))).GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "DELETE", $"/api/ResumeSkills/{resumeId}/{skillId}")).StatusCode);

            // the removed row is brought back instead of hitting the table's key
            Assert.Equal(HttpStatusCode.OK, (await add()).StatusCode);
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Resume_Skills WHERE resume_id = @resumeId AND skill_id = @skillId", new { resumeId, skillId }));
            Assert.Equal(1, (await Json(await Call(As(a), "GET", $"/api/ResumeSkills/by-resume/{resumeId}"))).GetArrayLength());
        }

        [DbFact]
        public async Task A_skill_that_does_not_exist_is_refused()
        {
            var a = NewUser();
            var resumeId = await CreateResume(a);

            var response = await Call(As(a), "POST", "/api/ResumeSkills", $$"""{"resumeId":{{resumeId}},"skillId":2000000000}""");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, Count("SELECT COUNT(*) FROM Resume_Skills WHERE resume_id = @resumeId", new { resumeId }));
        }

        [DbFact]
        public async Task Nobody_else_can_list_add_or_remove_the_skills_on_your_resume()
        {
            var a = NewUser();
            var b = NewUser();
            var resumeId = await CreateResume(a);
            var skillId = NewSkill("sql");

            await Call(As(a), "POST", "/api/ResumeSkills", $$"""{"resumeId":{{resumeId}},"skillId":{{skillId}}}""");

            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "GET", $"/api/ResumeSkills/by-resume/{resumeId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "POST", "/api/ResumeSkills", $$"""{"resumeId":{{resumeId}},"skillId":{{NewSkill("extra")}}}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "DELETE", $"/api/ResumeSkills/{resumeId}/{skillId}")).StatusCode);

            Assert.Equal(1, Count("SELECT COUNT(*) FROM Resume_Skills WHERE resume_id = @resumeId", new { resumeId }));
            Assert.Equal(0, Count("SELECT CAST(is_deleted AS int) FROM Resume_Skills WHERE resume_id = @resumeId AND skill_id = @skillId", new { resumeId, skillId }));
        }

        // ----- job preferences --------------------------------------------------

        [DbFact]
        public async Task Preferences_are_one_row_per_user_and_belong_to_the_caller()
        {
            var a = NewUser();
            var b = NewUser();

            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "GET", $"/api/JobPreference/by-user/{a}")).StatusCode);

            // Values nobody else has, so an answer that comes from the wrong row can't look right.
            await Call(As(a), "PUT", $"/api/JobPreference/by-user/{a}", $$"""{"preferredLocation":"Taguig-{{_tag}}","workArrangement":"Remote","minSalary":30111,"maxSalary":60111,"userId":{{b}},"isDeleted":true}""");
            var second = await Call(As(a), "PUT", $"/api/JobPreference/by-user/{a}", $$"""{"preferredLocation":"Makati-{{_tag}}","workArrangement":"hybrid","minSalary":41234,"maxSalary":71234}""");
            var body = await Json(second);

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(($"Makati-{_tag}", "hybrid", 41234m, a), (body.GetProperty("preferredLocation").GetString(), body.GetProperty("workArrangement").GetString(), body.GetProperty("minSalary").GetDecimal(), body.GetProperty("userId").GetInt32()));
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Job_Preferences WHERE user_id = @a AND is_deleted = 0", new { a }));
            Assert.Equal(0, Count("SELECT COUNT(*) FROM Job_Preferences WHERE user_id = @b", new { b }));

            // and the other user's save answers with their own row, not the first one in the table
            var bSaved = await Json(await Call(As(b), "PUT", $"/api/JobPreference/by-user/{b}", $$"""{"preferredLocation":"Cebu-{{_tag}}"}"""));

            Assert.Equal(($"Cebu-{_tag}", b), (bSaved.GetProperty("preferredLocation").GetString(), bSaved.GetProperty("userId").GetInt32()));
            Assert.Equal($"Makati-{_tag}", (await Json(await Call(As(a), "GET", $"/api/JobPreference/by-user/{a}"))).GetProperty("preferredLocation").GetString());
        }

        [DbFact]
        public async Task You_cannot_read_or_change_someone_elses_preferences()
        {
            var a = NewUser();
            var b = NewUser();

            await Call(As(a), "PUT", $"/api/JobPreference/by-user/{a}", """{"preferredLocation":"Taguig"}""");

            Assert.Equal(HttpStatusCode.Forbidden, (await Call(As(b), "GET", $"/api/JobPreference/by-user/{a}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Call(As(b), "PUT", $"/api/JobPreference/by-user/{a}", """{"preferredLocation":"HACKED"}""")).StatusCode);
            Assert.Equal("Taguig", Value<string>("SELECT preferred_location FROM Job_Preferences WHERE user_id = @a", new { a }));
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "GET", $"/api/JobPreference/by-user/{b}")).StatusCode);
        }
    }
}
