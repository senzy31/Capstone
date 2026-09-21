using System.Net;
using System.Text.Json;
using Dapper;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Matching;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Resumes;
using Joblink.Tests.Support;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // The real database behind recommendations: which resume, which skills, which job title and which preferences
    // a job seeker is scored on - and, end to end, that it is the caller's own. Needs JOBLINK_TEST_DB=1; every test
    // makes its own users and skills and removes them afterwards.
    public sealed class RecommendationsDbTests : IClassFixture<DbApiFactory>, IDisposable
    {
        private readonly DbApiFactory _factory;
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();
        private readonly List<int> _skillIds = new();
        private readonly SqlScoringProfileReader _reader = new(new ResumeDataStore(DbConfig.DefaultConnectionString), new SkillStore(DbConfig.DefaultConnectionString));

        public RecommendationsDbTests(DbApiFactory factory) => _factory = factory;

        // ----- helpers ----------------------------------------------------------------------------------

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private int NewUser()
        {
            using var db = Open();

            var id = db.QuerySingle<int>(
                @"INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted)
                  VALUES (@name, @email, 'x', 'user', GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest {_tag}", email = $"dbtest.{_tag}.{_userIds.Count}@example.com" });

            _userIds.Add(id);

            return id;
        }

        private int NewResume(int userId)
        {
            using var db = Open();

            return db.QuerySingle<int>(
                "INSERT INTO Resumes (user_id, title, created_at, is_deleted) VALUES (@userId, 'r', GETDATE(), 0); SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { userId });
        }

        // Skill names carry the test's tag so they can be removed; the reader returns them as stored.
        private string Tagged(string name) => $"dbtest-{_tag}-{name}";

        private int NewSkill(string storedName)
        {
            using var db = Open();

            var id = db.QuerySingle<int>("INSERT INTO Skills (skill_name, is_deleted) VALUES (@storedName, 0); SELECT CAST(SCOPE_IDENTITY() AS int);", new { storedName });

            _skillIds.Add(id);

            return id;
        }

        private void Link(int resumeId, int skillId, bool deleted = false)
        {
            using var db = Open();

            db.Execute("INSERT INTO Resume_Skills (resume_id, skill_id, is_deleted) VALUES (@resumeId, @skillId, @deleted)", new { resumeId, skillId, deleted });
        }

        private void Experience(int resumeId, string? position, string? start, string? end, bool deleted = false)
        {
            using var db = Open();

            db.Execute(
                "INSERT INTO Experience (resume_id, company_name, position, start_date, end_date, is_deleted) VALUES (@resumeId, 'Acme', @position, @start, @end, @deleted)",
                new { resumeId, position, start, end, deleted });
        }

        private void Preferences(int userId, string? place, string? arrangement, decimal? min, decimal? max, bool deleted = false)
        {
            using var db = Open();

            db.Execute(
                "INSERT INTO Job_Preferences (user_id, preferred_location, work_arrangement, min_salary, max_salary, is_deleted) VALUES (@userId, @place, @arrangement, @min, @max, @deleted)",
                new { userId, place, arrangement, min, max, deleted });
        }

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Resume_Skills WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Experience    WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Resumes WHERE user_id IN @users;
                DELETE FROM Job_Preferences WHERE user_id IN @users;
                DELETE FROM Skills WHERE skill_name LIKE @skills OR skill_id IN @skillIds;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users, skills = $"dbtest-{_tag}-%", skillIds = _skillIds.Count > 0 ? _skillIds : new List<int> { -1 } });
        }

        // ----- the skills ---------------------------------------------------------------------------------

        [DbFact]
        public void Skills_come_from_the_first_resume_only_in_skill_id_order()
        {
            var user = NewUser();
            var first = NewResume(user);
            var second = NewResume(user);
            var a = NewSkill(Tagged("A"));
            var b = NewSkill(Tagged("B"));
            var c = NewSkill(Tagged("C"));

            Link(first, b);
            Link(first, a);
            Link(second, c);

            Assert.Equal(new[] { Tagged("A"), Tagged("B") }, _reader.Read(user).Skills);
        }

        [DbFact]
        public void A_removed_skill_link_and_a_deleted_skill_are_left_out()
        {
            var user = NewUser();
            var resume = NewResume(user);
            var kept = NewSkill(Tagged("Kept"));
            var removedLink = NewSkill(Tagged("RemovedLink"));
            var deletedSkill = NewSkill(Tagged("DeletedSkill"));

            Link(resume, kept);
            Link(resume, removedLink, deleted: true);
            Link(resume, deletedSkill);

            using (var db = Open())
                db.Execute("UPDATE Skills SET is_deleted = 1 WHERE skill_id = @deletedSkill", new { deletedSkill });

            Assert.Equal(new[] { Tagged("Kept") }, _reader.Read(user).Skills);
        }

        [DbFact]
        public void Names_are_trimmed_blanks_dropped_and_the_same_spelling_counted_once()
        {
            var user = NewUser();
            var resume = NewResume(user);

            Link(resume, NewSkill(Tagged("Padded") + "  "));
            Link(resume, NewSkill(Tagged("Padded")));                    // the same after trimming
            Link(resume, NewSkill(Tagged("padded")));                    // a different spelling stays
            Link(resume, NewSkill("   "));                               // blank
            Link(resume, NewSkill(Tagged("Other")));

            Assert.Equal(new[] { Tagged("Padded"), Tagged("padded"), Tagged("Other") }, _reader.Read(user).Skills);
        }

        [DbFact]
        public void When_the_first_resume_is_deleted_the_next_one_becomes_the_first()
        {
            var user = NewUser();
            var first = NewResume(user);
            var second = NewResume(user);

            Link(first, NewSkill(Tagged("Old")));
            Link(second, NewSkill(Tagged("New")));

            using (var db = Open())
                db.Execute("UPDATE Resumes SET is_deleted = 1 WHERE resume_id = @first", new { first });

            Assert.Equal(new[] { Tagged("New") }, _reader.Read(user).Skills);
        }

        [DbFact]
        public void Nobody_elses_resume_is_ever_read()
        {
            var me = NewUser();
            var someoneElse = NewUser();

            Link(NewResume(me), NewSkill(Tagged("Mine")));
            Link(NewResume(someoneElse), NewSkill(Tagged("Theirs")));

            Assert.Equal(new[] { Tagged("Mine") }, _reader.Read(me).Skills);
            Assert.Equal(new[] { Tagged("Theirs") }, _reader.Read(someoneElse).Skills);
        }

        [DbFact]
        public void A_job_seeker_with_no_resume_or_no_skills_has_none_but_keeps_their_preferences()
        {
            var noResume = NewUser();
            var noSkills = NewUser();
            NewResume(noSkills);
            Preferences(noResume, "Cebu", "remote", 20000, 40000);

            Assert.Empty(_reader.Read(noSkills).Skills);
            Assert.Null(_reader.Read(noSkills).Preferences);

            var profile = _reader.Read(noResume);

            Assert.Empty(profile.Skills);
            Assert.Equal("", profile.Role);
            Assert.Equal(new ScoringPreferences("Cebu", "remote", 20000, 40000), profile.Preferences);
        }

        // ----- the latest job title -------------------------------------------------------------------------

        [DbFact]
        public void The_role_is_the_current_job_otherwise_the_latest_to_end_and_deleted_entries_do_not_count()
        {
            var user = NewUser();
            var resume = NewResume(user);

            Experience(resume, "Intern", "2020-01-01", "2020-06-01");
            Experience(resume, "Cashier", "2019-01-01", "2019-12-01");
            Experience(resume, "Frontend Developer", "2021-01-01", null);
            Experience(resume, "Deleted Current Job", "2022-01-01", null, deleted: true);
            Experience(resume, "   ", "2023-01-01", null);

            Assert.Equal("Frontend Developer", _reader.Read(user).Role);
        }

        [DbFact]
        public void Without_a_current_job_the_one_that_ended_last_is_the_role()
        {
            var user = NewUser();
            var resume = NewResume(user);

            Experience(resume, "Intern", "2020-01-01", "2020-06-01");
            Experience(resume, "Analyst", "2020-07-01", "2021-03-01");

            Assert.Equal("Analyst", _reader.Read(user).Role);
        }

        // ----- preferences -----------------------------------------------------------------------------------

        [DbFact]
        public void Preferences_are_read_with_their_salaries_as_the_numbers_a_browser_would_have_read()
        {
            var user = NewUser();
            Preferences(user, "Makati, Cebu", "onsite", 30000.50m, 60000m);

            Assert.Equal(new ScoringPreferences("Makati, Cebu", "onsite", 30000.5, 60000), _reader.Read(user).Preferences);
        }

        [DbFact]
        public void Empty_salaries_are_zero_and_a_deleted_preference_is_ignored()
        {
            var user = NewUser();
            var other = NewUser();
            Preferences(user, "Davao", null, null, null);
            Preferences(other, "Manila", "hybrid", 1, 2, deleted: true);

            Assert.Equal(new ScoringPreferences("Davao", null, 0, 0), _reader.Read(user).Preferences);
            Assert.Null(_reader.Read(other).Preferences);
        }

        // ----- end to end: real database, real plan rules, the job feed faked -----------------------------------------

        [DbFact]
        public async Task The_endpoint_scores_the_callers_own_saved_resume_and_preferences_and_their_plan_decides_how_much_is_shown()
        {
            var user = NewUser();
            var resume = NewResume(user);
            Link(resume, NewSkill(Tagged("React")));
            Link(resume, NewSkill(Tagged("SQL")));
            Experience(resume, "Frontend Developer", "2021-01-01", null);
            Preferences(user, "Makati", null, 30000, 60000);

            _factory.Search.Returns(
                new Dictionary<string, object?> { ["job_id"] = "1", ["joblink_job_id"] = 1, ["job_title"] = "Chef", ["job_description"] = "Lots of cooking.", ["job_city"] = "Manila", ["job_country"] = "PH", ["job_min_salary"] = 20000, ["job_max_salary"] = 25000, ["job_salary_period"] = "MONTH" },
                new Dictionary<string, object?> { ["job_id"] = "2", ["joblink_job_id"] = 2, ["job_title"] = "React SQL Developer", ["job_description"] = $"We use {Tagged("React")} and {Tagged("SQL")} every day.", ["job_city"] = "Makati", ["job_country"] = "PH", ["job_min_salary"] = 40000, ["job_max_salary"] = 50000, ["job_salary_period"] = "MONTH" });

            var client = _factory.ClientFor(user);

            var free = JsonDocument.Parse(await client.GetStringAsync("/api/Recommendations")).RootElement;

            Assert.Equal("Frontend Developer jobs in Makati", free.GetProperty("query").GetString());
            Assert.Equal(2, free.GetProperty("skillCount").GetInt32());
            Assert.True(free.GetProperty("hasPreferences").GetBoolean());
            Assert.Equal(new[] { 100, 17 }, free.GetProperty("data").EnumerateArray().Select(j => j.GetProperty("joblink_match").GetProperty("score").GetInt32()).ToArray());
            Assert.False(free.GetProperty("data")[0].GetProperty("joblink_match").TryGetProperty("skills", out _));

            _factory.MakePremium(user);

            var premium = JsonDocument.Parse(await client.GetStringAsync("/api/Recommendations")).RootElement;

            Assert.Equal(new[] { Tagged("React"), Tagged("SQL") }, premium.GetProperty("data")[0].GetProperty("joblink_match").GetProperty("skills").GetProperty("matched").EnumerateArray().Select(m => m.GetString()).ToArray());
        }
    }
}
