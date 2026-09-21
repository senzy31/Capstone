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
    // The plan limits on the real API and the real Joblinkv2 database: how many resumes a user
    // may save, how many jobs they may save. Free and Premium, an upgrade taking effect with the
    // same login token, Premium running out, and what the limits do to rows that already exist.
    // The limit is decided on the server, from the plan in the database - never by a hidden button.
    public sealed class PlanLimitDbTests : IClassFixture<DbApiFactory>, IDisposable
    {
        private readonly DbApiFactory _factory;
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();

        public PlanLimitDbTests(DbApiFactory factory) => _factory = factory;

        // ----- helpers ----------------------------------------------------------

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

        private List<int> NewJobs(int count)
        {
            using var db = Open();

            return Enumerable.Range(0, count).Select(_ => db.QuerySingle<int>(
                @"INSERT INTO Job_Listings (external_job_id, title, company, source_api, is_deleted, source)
                  VALUES (@ext, 'DB test job', 'Acme', 'manual', 0, 'External');
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { ext = $"dbtest-{_tag}-{Guid.NewGuid():N}" })).ToList();
        }

        private HttpClient As(int userId) => _factory.ClientFor(userId);

        private static Task<HttpResponseMessage> Call(HttpClient client, string method, string url, string? json = null) =>
            client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
            });

        private static async Task<JsonElement> Json(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private int Count(string sql, object? args = null)
        {
            using var db = Open();
            return db.QuerySingle<int>(sql, args);
        }

        private Task<HttpResponseMessage> NewResume(HttpClient client, string title = "CV") =>
            Call(client, "POST", "/api/Resume", $"{{\"title\":\"{title}\"}}");

        private Task<HttpResponseMessage> Save(HttpClient client, int jobId) =>
            Call(client, "POST", "/api/SavedJobs", $"{{\"jobId\":{jobId}}}");

        private Task<HttpResponseMessage> Upgrade(HttpClient client) =>
            Call(client, "POST", "/api/Subscription/upgrade", "{\"billing\":\"Monthly\"}");

        private int LiveResumes(int user) => Count("SELECT COUNT(*) FROM Resumes WHERE user_id = @user AND is_deleted = 0", new { user });

        private int LiveSaved(int user) => Count("SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @user AND is_deleted = 0", new { user });

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Saved_Jobs WHERE user_id IN @users;
                DELETE FROM Resume_Skills WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Education     WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Experience    WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Resumes WHERE user_id IN @users;
                DELETE FROM Job_Listings WHERE external_job_id LIKE @tag;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users, tag = $"dbtest-{_tag}-%" });
        }

        // ----- saved resumes ------------------------------------------------------

        [DbFact]
        public async Task A_free_user_keeps_one_resume_and_the_second_is_a_403_that_asks_to_upgrade()
        {
            var user = NewUser();
            var client = As(user);

            Assert.Equal(HttpStatusCode.OK, (await NewResume(client)).StatusCode);

            var refused = await NewResume(client);
            var body = await Json(refused);

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.True(body.GetProperty("upgradeRequired").GetBoolean());
            Assert.Equal("upgrade_required", body.GetProperty("code").GetString());
            Assert.Equal("resumeVersions", body.GetProperty("feature").GetString());
            Assert.Equal(1, body.GetProperty("limit").GetInt32());
            Assert.Contains("Upgrade to Premium", body.GetProperty("message").GetString());
            Assert.Equal(1, LiveResumes(user));
        }

        [DbFact]
        public async Task Upgrading_lifts_the_limit_at_once_with_the_same_token_up_to_ten_and_no_further()
        {
            var user = NewUser();
            var client = As(user);      // one client = one token, made while still Free

            await NewResume(client);
            Assert.Equal(HttpStatusCode.Forbidden, (await NewResume(client)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await Upgrade(client)).StatusCode);

            for (var i = 2; i <= 10; i++)
                Assert.Equal(HttpStatusCode.OK, (await NewResume(client)).StatusCode);

            var eleventh = await NewResume(client);
            var body = await Json(eleventh);

            Assert.Equal(HttpStatusCode.Forbidden, eleventh.StatusCode);
            Assert.False(body.GetProperty("upgradeRequired").GetBoolean());       // there is nothing above Premium to upgrade to
            Assert.Equal("limit_reached", body.GetProperty("code").GetString());
            Assert.Equal(10, body.GetProperty("limit").GetInt32());
            Assert.Equal(10, LiveResumes(user));
        }

        [DbFact]
        public async Task Deleting_a_resume_frees_the_slot()
        {
            var client = As(NewUser());

            var created = await Json(await NewResume(client, "First"));
            Assert.Equal(HttpStatusCode.Forbidden, (await NewResume(client, "Second")).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await Call(client, "DELETE", $"/api/Resume?id={created.GetProperty("resumeId").GetInt32()}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await NewResume(client, "Second")).StatusCode);
        }

        [DbFact]
        public async Task When_premium_runs_out_the_resumes_are_kept_and_editable_but_no_more_can_be_added()
        {
            var user = NewUser();
            var client = As(user);

            await Upgrade(client);
            var ids = new List<int>();

            for (var i = 0; i < 3; i++)
                ids.Add((await Json(await NewResume(client, $"CV{i}"))).GetProperty("resumeId").GetInt32());

            _factory.Clock.Advance(TimeSpan.FromDays(40));       // Premium ran out - nothing was run

            // what they have is still theirs: read, edit, delete all work
            Assert.Equal(3, (await Json(await Call(client, "GET", $"/api/Resume/by-user/{user}"))).GetArrayLength());
            Assert.Equal(HttpStatusCode.OK, (await Call(client, "PUT", "/api/Resume", $"{{\"resumeId\":{ids[0]},\"title\":\"Renamed\"}}")).StatusCode);

            // but they can't add another, even though only 3 are over the Free cap of 1
            var refused = await NewResume(client, "New");

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.True((await Json(refused)).GetProperty("upgradeRequired").GetBoolean());

            // deleting down to below the cap makes room again
            await Call(client, "DELETE", $"/api/Resume?id={ids[1]}");
            Assert.Equal(HttpStatusCode.Forbidden, (await NewResume(client, "New")).StatusCode);   // 2 left: still at or over 1
            await Call(client, "DELETE", $"/api/Resume?id={ids[2]}");
            Assert.Equal(HttpStatusCode.Forbidden, (await NewResume(client, "New")).StatusCode);   // 1 left: at the cap
            await Call(client, "DELETE", $"/api/Resume?id={ids[0]}");
            Assert.Equal(HttpStatusCode.OK, (await NewResume(client, "New")).StatusCode);          // 0 left
        }

        [DbFact]
        public async Task Twenty_parallel_creates_by_one_free_user_leave_exactly_one_resume()
        {
            var user = NewUser();
            var client = As(user);
            ThreadPool.SetMinThreads(100, 100);

            var statuses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () => (await NewResume(client)).StatusCode)));

            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
            Assert.Equal(19, statuses.Count(s => s == HttpStatusCode.Forbidden));
            Assert.Equal(1, LiveResumes(user));
        }

        // ----- saved jobs ---------------------------------------------------------

        [DbFact]
        public async Task A_free_user_saves_ten_jobs_and_the_eleventh_is_a_403_that_asks_to_upgrade()
        {
            var user = NewUser();
            var client = As(user);
            var jobs = NewJobs(11);

            foreach (var job in jobs.Take(10))
                Assert.Equal(HttpStatusCode.OK, (await Save(client, job)).StatusCode);

            var refused = await Save(client, jobs[10]);
            var body = await Json(refused);

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.True(body.GetProperty("upgradeRequired").GetBoolean());
            Assert.Equal("savedJobs", body.GetProperty("feature").GetString());
            Assert.Equal(10, body.GetProperty("limit").GetInt32());
            Assert.Contains("unlimited saved jobs", body.GetProperty("message").GetString());
            Assert.Equal(10, LiveSaved(user));
        }

        [DbFact]
        public async Task Saving_a_job_that_is_already_saved_is_fine_at_the_limit_and_un_saving_makes_room()
        {
            var client = As(NewUser());
            var jobs = NewJobs(11);

            foreach (var job in jobs.Take(10))
                await Save(client, job);

            Assert.Equal(HttpStatusCode.OK, (await Save(client, jobs[0])).StatusCode);          // already saved: no new slot needed
            Assert.Equal(HttpStatusCode.Forbidden, (await Save(client, jobs[10])).StatusCode);

            await Call(client, "DELETE", $"/api/SavedJobs/{jobs[3]}");

            Assert.Equal(HttpStatusCode.OK, (await Save(client, jobs[10])).StatusCode);
        }

        [DbFact]
        public async Task Bringing_back_a_removed_saved_job_needs_a_free_slot_like_any_other()
        {
            var user = NewUser();
            var client = As(user);
            var jobs = NewJobs(11);

            foreach (var job in jobs.Take(10))
                await Save(client, job);

            await Call(client, "DELETE", $"/api/SavedJobs/{jobs[0]}");                    // 9 live, one removed
            Assert.Equal(HttpStatusCode.OK, (await Save(client, jobs[10])).StatusCode);  // 10 live again

            var refused = await Save(client, jobs[0]);                                    // re-saving the removed one would be the 11th

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(10, LiveSaved(user));
        }

        [DbFact]
        public async Task Premium_saves_without_a_limit_and_keeps_them_when_it_runs_out()
        {
            var user = NewUser();
            var client = As(user);
            var jobs = NewJobs(13);

            await Upgrade(client);

            foreach (var job in jobs.Take(12))
                Assert.Equal(HttpStatusCode.OK, (await Save(client, job)).StatusCode);

            _factory.Clock.Advance(TimeSpan.FromDays(40));     // Premium ran out

            Assert.Equal(12, (await Json(await Call(client, "GET", "/api/SavedJobs"))).GetArrayLength());     // all still there
            Assert.Equal(HttpStatusCode.Forbidden, (await Save(client, jobs[12])).StatusCode);                // but no more

            for (var i = 0; i < 3; i++)                                                                       // 12 -> 9: back under the cap
                await Call(client, "DELETE", $"/api/SavedJobs/{jobs[i]}");

            Assert.Equal(HttpStatusCode.OK, (await Save(client, jobs[12])).StatusCode);
        }

        [DbFact]
        public async Task Thirty_parallel_saves_by_one_free_user_leave_exactly_ten()
        {
            var user = NewUser();
            var client = As(user);
            var jobs = NewJobs(30);
            ThreadPool.SetMinThreads(100, 100);

            var statuses = await Task.WhenAll(jobs.Select(job => Task.Run(async () => (await Save(client, job)).StatusCode)));

            Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.OK));
            Assert.Equal(20, statuses.Count(s => s == HttpStatusCode.Forbidden));
            Assert.Equal(10, LiveSaved(user));
        }

        [DbFact]
        public async Task One_users_limit_does_not_touch_another_user()
        {
            var full = As(NewUser());
            var other = As(NewUser());
            var jobs = NewJobs(11);

            foreach (var job in jobs.Take(10))
                await Save(full, job);

            Assert.Equal(HttpStatusCode.Forbidden, (await Save(full, jobs[10])).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Save(other, jobs[10])).StatusCode);
        }

        // ----- the plan endpoint reports what the limits count ------------------------

        [DbFact]
        public async Task The_plan_endpoint_reports_real_usage_against_the_limits()
        {
            var user = NewUser();
            var client = As(user);
            var jobs = NewJobs(3);

            // someone else has plenty saved: it must not show up in this user's usage
            var other = As(NewUser());
            _factory.MakePremium(_userIds[^1]);
            await NewResume(other);
            await NewResume(other);
            foreach (var job in NewJobs(5))
                await Save(other, job);

            await NewResume(client);

            foreach (var job in jobs)
                await Save(client, job);

            var body = await Json(await Call(client, "GET", "/api/Subscription"));

            Assert.Equal(1, body.GetProperty("usage").GetProperty("resumeVersions").GetInt32());
            Assert.Equal(3, body.GetProperty("usage").GetProperty("savedJobs").GetInt32());
            Assert.Equal(1, body.GetProperty("limits").GetProperty("resumeVersions").GetInt32());
            Assert.Equal(10, body.GetProperty("limits").GetProperty("savedJobs").GetInt32());
        }
    }
}
