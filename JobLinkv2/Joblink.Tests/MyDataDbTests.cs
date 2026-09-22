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
    // Two real users, the real API and the real Joblinkv2 database, for notifications,
    // saved jobs, job matches and skills. Each test makes its own users, notifications,
    // jobs and skills and deletes everything it made.
    public sealed class MyDataDbTests : IClassFixture<DbApiFactory>, IDisposable
    {
        private readonly DbApiFactory _factory;
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();

        public MyDataDbTests(DbApiFactory factory) => _factory = factory;

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

        private int NewNotification(int userId, string message, DateTime? createdAt = null)
        {
            using var db = Open();

            return db.QuerySingle<int>(
                @"INSERT INTO Notifications (user_id, message, is_read, created_at, is_deleted)
                  VALUES (@userId, CAST(@message AS varchar(max)), 0, @createdAt, 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { userId, message, createdAt = createdAt ?? DateTime.Now });
        }

        private int NewJob()
        {
            using var db = Open();

            return db.QuerySingle<int>(
                @"INSERT INTO Job_Listings (external_job_id, title, company, source_api, is_deleted, source)
                  VALUES (@ext, 'DB test job', 'Acme', 'manual', 0, 'External');
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { ext = $"dbtest-{_tag}-{Guid.NewGuid():N}" });
        }

        private int NewMatch(int userId, int jobId, decimal score)
        {
            using var db = Open();

            return db.QuerySingle<int>(
                @"INSERT INTO Job_Match (user_id, job_id, match_score, created_at, is_deleted)
                  VALUES (@userId, @jobId, @score, GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { userId, jobId, score });
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

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Job_Match WHERE user_id IN @users;
                DELETE FROM Saved_Jobs WHERE user_id IN @users;
                DELETE FROM Notifications WHERE user_id IN @users;
                DELETE FROM Job_Listings WHERE external_job_id LIKE @tag;
                DELETE FROM Skills WHERE skill_name LIKE @skills;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users, tag = $"dbtest-{_tag}-%", skills = $"dbtest-{_tag}-%" });
        }

        // ----- notifications ----------------------------------------------------

        [DbFact]
        public async Task You_see_only_your_own_notifications_newest_first()
        {
            var a = NewUser();
            var b = NewUser();
            var older = NewNotification(a, "older", DateTime.Now.AddHours(-2));
            var newer = NewNotification(a, "newer", DateTime.Now.AddHours(-1));
            NewNotification(b, "SECRET for b");

            var response = await Json(await Call(As(a), "GET", "/api/Notification"));
            var list = response.GetProperty("data");

            Assert.Equal(new[] { newer, older }, list.EnumerateArray().Select(n => n.GetProperty("notificationId").GetInt32()).ToArray());
            Assert.DoesNotContain("SECRET", response.GetRawText());
        }

        [DbFact]
        public async Task An_employer_reads_the_notifications_the_server_made_for_them()
        {
            var boss = NewUser("employer");
            NewNotification(boss, "Maria applied to your job");

            var response = await Json(await Call(As(boss, "employer"), "GET", "/api/Notification"));

            Assert.Equal("Maria applied to your job", response.GetProperty("data")[0].GetProperty("message").GetString());
        }

        [DbFact]
        public async Task Nobody_else_can_read_change_or_delete_your_notification()
        {
            var a = NewUser();
            var b = NewUser();
            var id = NewNotification(a, "for a only");

            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "GET", $"/api/Notification/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "PUT", "/api/Notification", $$"""{"notificationId":{{id}},"isRead":true}""")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "DELETE", $"/api/Notification?id={id}")).StatusCode);

            var row = Value<(bool IsRead, bool Deleted, string Message)>(
                "SELECT CAST(is_read AS bit), CAST(is_deleted AS bit), CAST(message AS varchar(100)) FROM Notifications WHERE notification_id = @id", new { id });

            Assert.Equal((false, false, "for a only"), row);
        }

        [DbFact]
        public async Task Marking_a_notification_read_changes_only_the_read_flag()
        {
            var a = NewUser();
            var b = NewUser();
            var id = NewNotification(a, "original text");

            var response = await Call(As(a), "PUT", "/api/Notification",
                $$"""{"notificationId":{{id}},"isRead":true,"message":"HACKED","userId":{{b}},"isDeleted":true,"createdAt":"2000-01-01"}""");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var row = Value<(int UserId, bool IsRead, bool Deleted, string Message)>(
                "SELECT user_id, CAST(is_read AS bit), CAST(is_deleted AS bit), CAST(message AS varchar(100)) FROM Notifications WHERE notification_id = @id", new { id });

            Assert.Equal((a, true, false, "original text"), row);

            // and back to unread, then delete: it leaves the list
            await Call(As(a), "PUT", "/api/Notification", $$"""{"notificationId":{{id}},"isRead":false}""");
            Assert.False(Value<bool>("SELECT CAST(is_read AS bit) FROM Notifications WHERE notification_id = @id", new { id }));

            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "DELETE", $"/api/Notification?id={id}")).StatusCode);
            Assert.Equal(0, (await Json(await Call(As(a), "GET", "/api/Notification"))).GetProperty("data").GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "GET", $"/api/Notification/{id}")).StatusCode);
        }

        [DbFact]
        public async Task Paging_reports_the_total_and_unread_counts_alongside_the_page_you_asked_for()
        {
            var a = NewUser();
            var ids = Enumerable.Range(1, 25).Select(i => NewNotification(a, $"msg {i}", DateTime.Now.AddMinutes(-i))).ToList();

            var page1 = await Json(await Call(As(a), "GET", "/api/Notification?page=1"));
            var page2 = await Json(await Call(As(a), "GET", "/api/Notification?page=2"));

            Assert.Equal(20, page1.GetProperty("data").GetArrayLength());
            Assert.Equal(5, page2.GetProperty("data").GetArrayLength());
            Assert.Equal(25, page1.GetProperty("totalCount").GetInt32());
            Assert.Equal(25, page1.GetProperty("unreadCount").GetInt32());

            // Page 1 is the newest 20 (the notifications were created oldest-minute-offset last).
            Assert.Equal(ids[0], page1.GetProperty("data")[0].GetProperty("notificationId").GetInt32());
            Assert.Equal(ids[20], page2.GetProperty("data")[0].GetProperty("notificationId").GetInt32());
        }

        [DbFact]
        public async Task Unread_count_and_mark_read_and_mark_all_read_work_and_stay_private()
        {
            var a = NewUser();
            var b = NewUser();
            var first = NewNotification(a, "one");
            var second = NewNotification(a, "two");
            NewNotification(b, "not yours");

            Assert.Equal(2, (await Json(await Call(As(a), "GET", "/api/Notification/unread-count"))).GetProperty("count").GetInt32());

            // Marking b's notification read from a's session doesn't exist as far as a is concerned.
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "PATCH", $"/api/Notification/{Value<int>("SELECT notification_id FROM Notifications WHERE user_id = @b", new { b })}/read")).StatusCode);

            var marked = await Json(await Call(As(a), "PATCH", $"/api/Notification/{first}/read"));
            Assert.True(marked.GetProperty("isRead").GetBoolean());
            Assert.Equal(1, (await Json(await Call(As(a), "GET", "/api/Notification/unread-count"))).GetProperty("count").GetInt32());

            var markAll = await Json(await Call(As(a), "PATCH", "/api/Notification/read-all"));
            Assert.Equal(1, markAll.GetProperty("updated").GetInt32());   // only "two" was still unread
            Assert.Equal(0, (await Json(await Call(As(a), "GET", "/api/Notification/unread-count"))).GetProperty("count").GetInt32());

            // b is unaffected by a's mark-all-read.
            Assert.Equal(1, (await Json(await Call(As(b), "GET", "/api/Notification/unread-count"))).GetProperty("count").GetInt32());

            Assert.True(Value<bool>("SELECT CAST(is_read AS bit) FROM Notifications WHERE notification_id = @second", new { second }));
        }

        // ----- saved jobs -------------------------------------------------------

        [DbFact]
        public async Task You_save_and_unsave_jobs_in_your_own_list_only()
        {
            var a = NewUser();
            var b = NewUser();
            var job = NewJob();

            // B saved this job once and removed it: their row stays (soft-deleted) and must stay removed
            await Call(As(b), "POST", "/api/SavedJobs", $$"""{"jobId":{{job}}}""");
            await Call(As(b), "DELETE", $"/api/SavedJobs/{job}");

            var saved = await Call(As(a), "POST", "/api/SavedJobs", $$"""{"jobId":{{job}},"userId":{{b}},"isDeleted":true}""");

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "POST", "/api/SavedJobs", $$"""{"jobId":{{job}}}""")).StatusCode);   // twice is fine
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @a AND job_id = @job AND is_deleted = 0", new { a, job }));
            Assert.Equal(0, Count("SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @b AND is_deleted = 0", new { b }));

            Assert.Equal(new[] { job }, (await Json(await Call(As(a), "GET", "/api/SavedJobs"))).EnumerateArray().Select(s => s.GetProperty("jobId").GetInt32()).ToArray());
            Assert.Equal(0, (await Json(await Call(As(b), "GET", "/api/SavedJobs"))).GetArrayLength());
            Assert.Equal(1, Count("SELECT CAST(is_deleted AS int) FROM Saved_Jobs WHERE user_id = @b AND job_id = @job", new { b, job }));   // A saving it did not bring B's back

            // B has not saved it, so B can't remove it; A's stays
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(b), "DELETE", $"/api/SavedJobs/{job}")).StatusCode);
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @a AND is_deleted = 0", new { a }));

            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "DELETE", $"/api/SavedJobs/{job}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "DELETE", $"/api/SavedJobs/{job}")).StatusCode);

            // saving it again brings the removed row back instead of hitting the table's key
            Assert.Equal(HttpStatusCode.OK, (await Call(As(a), "POST", "/api/SavedJobs", $$"""{"jobId":{{job}}}""")).StatusCode);
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @a AND job_id = @job", new { a, job }));
            Assert.Equal(0, Count("SELECT CAST(is_deleted AS int) FROM Saved_Jobs WHERE user_id = @a AND job_id = @job", new { a, job }));
        }

        [DbFact]
        public async Task You_cannot_save_a_job_that_does_not_exist()
        {
            var a = NewUser();

            var response = await Call(As(a), "POST", "/api/SavedJobs", """{"jobId":2000000000}""");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, Count("SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @a", new { a }));
        }

        // ----- job matches ------------------------------------------------------

        [DbFact]
        public async Task You_see_only_your_own_matches_best_first_and_cannot_write_any()
        {
            var a = NewUser();
            var b = NewUser();
            var job = NewJob();
            var low = NewMatch(a, job, 40.5m);
            var high = NewMatch(a, job, 88.0m);
            var bMatch = NewMatch(b, job, 99.0m);

            var list = await Json(await Call(As(a), "GET", "/api/JobMatch"));

            Assert.Equal(new[] { high, low }, list.EnumerateArray().Select(m => m.GetProperty("matchId").GetInt32()).ToArray());
            Assert.Equal(HttpStatusCode.NotFound, (await Call(As(a), "GET", $"/api/JobMatch/{bMatch}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Call(As(b), "GET", $"/api/JobMatch/{bMatch}")).StatusCode);

            // no way to give yourself a better score
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Call(As(a), "PUT", "/api/JobMatch", $$"""{"matchId":{{low}},"matchScore":100}""")).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Call(As(a), "POST", "/api/JobMatch", $$"""{"jobId":{{job}},"matchScore":100}""")).StatusCode);
            Assert.Equal(40.5m, Value<decimal>("SELECT match_score FROM Job_Match WHERE match_id = @low", new { low }));
        }

        // ----- skills -----------------------------------------------------------

        [DbFact]
        public async Task Anyone_can_read_the_skills_list()
        {
            var response = await Call(_factory.CreateClient(), "GET", "/api/Skills");
            var list = await Json(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(list.GetArrayLength() > 0);
            Assert.Equal(HttpStatusCode.OK, (await Call(_factory.CreateClient(), "GET", $"/api/Skills/{list[0].GetProperty("skillId").GetInt32()}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Call(_factory.CreateClient(), "GET", "/api/Skills/2000000000")).StatusCode);
        }

        [DbFact]
        public async Task A_skill_name_matches_whatever_the_capitals_and_spacing()
        {
            var a = NewUser();

            var first = await Json(await Call(As(a), "POST", "/api/Skills", $$"""{"skillName":"dbtest-{{_tag}}-C#"}"""));
            var second = await Json(await Call(As(a), "POST", "/api/Skills", $$"""{"skillName":"  DBTEST-{{_tag}}-c#  "}"""));

            Assert.Equal(first.GetProperty("skillId").GetInt32(), second.GetProperty("skillId").GetInt32());
            Assert.Equal($"dbtest-{_tag}-C#", second.GetProperty("skillName").GetString());   // the first spelling wins
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Skills WHERE skill_name LIKE @name", new { name = $"dbtest-{_tag}-%" }));
        }

        [DbFact]
        public async Task A_removed_skill_is_brought_back_not_duplicated()
        {
            var a = NewUser();
            var created = await Json(await Call(As(a), "POST", "/api/Skills", $$"""{"skillName":"dbtest-{{_tag}}-Go"}"""));
            var id = created.GetProperty("skillId").GetInt32();

            using (var db = Open())
                db.Execute("UPDATE Skills SET is_deleted = 1 WHERE skill_id = @id", new { id });

            var again = await Json(await Call(As(a), "POST", "/api/Skills", $$"""{"skillName":"dbtest-{{_tag}}-go"}"""));

            Assert.Equal(id, again.GetProperty("skillId").GetInt32());
            Assert.Equal(0, Count("SELECT CAST(is_deleted AS int) FROM Skills WHERE skill_id = @id", new { id }));
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Skills WHERE skill_name LIKE @name", new { name = $"dbtest-{_tag}-%" }));
        }

        [DbFact]
        public async Task Twenty_people_adding_the_same_new_skill_at_once_make_one_skill()
        {
            var users = Enumerable.Range(0, 20).Select(_ => NewUser()).ToList();
            ThreadPool.SetMinThreads(100, 100);

            var ids = await Task.WhenAll(users.Select(u => Task.Run(async () =>
            {
                var response = await Call(As(u), "POST", "/api/Skills", $$"""{"skillName":"dbtest-{{_tag}}-Rust"}""");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return (await Json(response)).GetProperty("skillId").GetInt32();
            })));

            Assert.Single(ids.Distinct());
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Skills WHERE skill_name LIKE @name", new { name = $"dbtest-{_tag}-%" }));
        }

        [DbFact]
        public async Task Skill_names_are_stored_exactly_as_typed_including_awkward_characters()
        {
            var a = NewUser();

            var created = await Json(await Call(As(a), "POST", "/api/Skills", JsonSerializer.Serialize(new { skillName = $"dbtest-{_tag}-'; DROP TABLE Skills; --" })));

            Assert.Equal($"dbtest-{_tag}-'; DROP TABLE Skills; --", created.GetProperty("skillName").GetString());
            Assert.True(Count("SELECT COUNT(*) FROM Skills") > 0);
        }
    }
}
