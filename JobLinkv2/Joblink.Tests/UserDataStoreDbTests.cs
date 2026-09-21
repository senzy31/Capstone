using Dapper;
using JobLinkv2.Repositories;
using JobLinkv2.Services.MyData;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // The stores on their own, called the way a careless caller might: as the wrong user,
    // with an id that isn't theirs. Every method has to refuse in SQL - the controllers
    // check first, but this layer must not depend on that.
    public sealed class UserDataStoreDbTests : IDisposable
    {
        private readonly UserDataStore _store = new(DbConfig.DefaultConnectionString);
        private readonly SkillStore _skills = new(DbConfig.DefaultConnectionString);
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();

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

        private int NewJob()
        {
            using var db = Open();

            return db.QuerySingle<int>(
                @"INSERT INTO Job_Listings (external_job_id, title, company, source_api, is_deleted, source)
                  VALUES (@ext, 'DB test job', 'Acme', 'manual', 0, 'External');
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { ext = $"dbtest-{_tag}-{Guid.NewGuid():N}" });
        }

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Job_Match WHERE user_id IN @users;
                DELETE FROM Saved_Jobs WHERE user_id IN @users;
                DELETE FROM Notifications WHERE user_id IN @users;
                DELETE FROM Job_Listings WHERE external_job_id LIKE @tag;
                DELETE FROM Skills WHERE skill_name LIKE @tag;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users, tag = $"dbtest-{_tag}-%" });
        }

        [DbFact]
        public void Notification_methods_refuse_the_wrong_user()
        {
            var (a, b) = (NewUser(), NewUser());
            int id;

            using (var db = Open())
                id = db.QuerySingle<int>(
                    @"INSERT INTO Notifications (user_id, message, is_read, created_at, is_deleted)
                      VALUES (@a, 'private', 0, GETDATE(), 0); SELECT CAST(SCOPE_IDENTITY() AS int);", new { a });

            Assert.Empty(_store.ListNotifications(b));
            Assert.Null(_store.GetNotification(b, id));
            Assert.False(_store.SetNotificationRead(b, id, true));
            Assert.False(_store.DeleteNotification(b, id));

            var still = _store.GetNotification(a, id)!;

            Assert.Equal(("private", false, a), (still.Message, still.IsRead, still.UserId));
            Assert.Single(_store.ListNotifications(a));
        }

        [DbFact]
        public void Saved_job_methods_are_per_user()
        {
            var (a, b) = (NewUser(), NewUser());
            var job = NewJob();

            // B saved and removed it: A saving it later must not bring B's row back
            _store.SaveJob(b, job);
            _store.UnsaveJob(b, job);

            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(a, job));
            Assert.Empty(_store.ListSavedJobs(b));
            Assert.Equal(SaveJobOutcome.JobNotFound, _store.SaveJob(a, 2_000_000_000));

            Assert.False(_store.UnsaveJob(b, job));
            Assert.Single(_store.ListSavedJobs(a));

            Assert.True(_store.UnsaveJob(a, job));
            Assert.Empty(_store.ListSavedJobs(a));
            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(a, job));
            Assert.Single(_store.ListSavedJobs(a));
        }

        [DbFact]
        public void Saving_stops_at_the_limit_but_already_saved_jobs_and_unlimited_plans_are_unaffected()
        {
            var (a, b) = (NewUser(), NewUser());
            var jobs = Enumerable.Range(0, 4).Select(_ => NewJob()).ToList();

            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(a, jobs[0], maxLive: 2));
            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(a, jobs[1], maxLive: 2));
            Assert.Equal(SaveJobOutcome.LimitReached, _store.SaveJob(a, jobs[2], maxLive: 2));
            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(a, jobs[0], maxLive: 2));           // already saved: no slot needed
            Assert.Equal(SaveJobOutcome.JobNotFound, _store.SaveJob(a, 2_000_000_000, maxLive: 2)); // a job that doesn't exist is that, not "full"
            Assert.Equal(2, _store.ListSavedJobs(a).Count);

            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(a, jobs[2], maxLive: null));         // no cap
            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(b, jobs[2], maxLive: 2));            // A being full doesn't block B
        }

        [DbFact]
        public void A_removed_saved_job_counts_again_when_it_is_brought_back()
        {
            var a = NewUser();
            var jobs = Enumerable.Range(0, 3).Select(_ => NewJob()).ToList();

            _store.SaveJob(a, jobs[0], maxLive: 2);
            _store.SaveJob(a, jobs[1], maxLive: 2);
            _store.UnsaveJob(a, jobs[0]);
            _store.SaveJob(a, jobs[2], maxLive: 2);                                                // 2 live again

            Assert.Equal(SaveJobOutcome.LimitReached, _store.SaveJob(a, jobs[0], maxLive: 2));     // bringing it back is the third
        }

        [DbFact]
        public void Parallel_saves_take_exactly_the_free_slots()
        {
            var a = NewUser();
            var jobs = Enumerable.Range(0, 20).Select(_ => NewJob()).ToList();
            ThreadPool.SetMinThreads(100, 100);

            var results = Task.WhenAll(jobs.Select(job => Task.Run(() => _store.SaveJob(a, job, maxLive: 5)))).GetAwaiter().GetResult();

            Assert.Equal(5, results.Count(r => r == SaveJobOutcome.Saved));
            Assert.Equal(15, results.Count(r => r == SaveJobOutcome.LimitReached));
            Assert.Equal(5, _store.ListSavedJobs(a).Count);
        }

        // Two people saving the same job is normal. One having it saved must not make the other's
        // save quietly do nothing, or count against them.
        [DbFact]
        public void Two_users_can_have_the_same_job_saved_at_once()
        {
            var (a, b) = (NewUser(), NewUser());
            var job = NewJob();

            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(b, job, maxLive: 5));
            Assert.Equal(SaveJobOutcome.Saved, _store.SaveJob(a, job, maxLive: 5));

            Assert.Equal(new[] { job }, _store.ListSavedJobs(a).Select(s => s.JobId).ToArray());   // A's save really happened
            Assert.Equal(new[] { job }, _store.ListSavedJobs(b).Select(s => s.JobId).ToArray());
            Assert.Equal(1, _store.CountSavedJobs(a));
            Assert.Equal(1, _store.CountSavedJobs(b));
        }

        [DbFact]
        public void Another_users_saves_neither_hide_nor_free_a_slot_at_the_limit()
        {
            var (a, b) = (NewUser(), NewUser());
            var jobs = Enumerable.Range(0, 3).Select(_ => NewJob()).ToList();

            _store.SaveJob(a, jobs[0], maxLive: 1);

            foreach (var job in jobs)
                _store.SaveJob(b, job, maxLive: null);

            // A is at the limit; B having every job saved changes nothing for A
            Assert.Equal(SaveJobOutcome.LimitReached, _store.SaveJob(a, jobs[1], maxLive: 1));
            Assert.Equal(1, _store.CountSavedJobs(a));
            Assert.Equal(3, _store.CountSavedJobs(b));
        }

        [DbFact]
        public void Match_methods_refuse_the_wrong_user()
        {
            var (a, b) = (NewUser(), NewUser());
            var job = NewJob();
            int id;

            using (var db = Open())
                id = db.QuerySingle<int>(
                    @"INSERT INTO Job_Match (user_id, job_id, match_score, created_at, is_deleted)
                      VALUES (@a, @job, 91.5, GETDATE(), 0); SELECT CAST(SCOPE_IDENTITY() AS int);", new { a, job });

            Assert.Empty(_store.ListMatches(b));
            Assert.Null(_store.GetMatch(b, id));
            Assert.Equal(91.5m, _store.GetMatch(a, id)!.MatchScore);
            Assert.Single(_store.ListMatches(a));
        }

        [DbFact]
        public void Skills_are_matched_without_regard_to_capitals_and_a_removed_one_comes_back()
        {
            var first = _skills.AddOrGet($"dbtest-{_tag}-Kotlin");
            var second = _skills.AddOrGet($"DBTEST-{_tag}-KOTLIN");

            Assert.Equal(first.SkillId, second.SkillId);

            using (var db = Open())
                db.Execute("UPDATE Skills SET is_deleted = 1 WHERE skill_id = @id", new { id = first.SkillId });

            Assert.Null(_skills.Get(first.SkillId));
            Assert.DoesNotContain(_skills.List(), s => s.SkillId == first.SkillId);

            var back = _skills.AddOrGet($"dbtest-{_tag}-kotlin");

            Assert.Equal(first.SkillId, back.SkillId);
            Assert.NotNull(_skills.Get(first.SkillId));
        }
    }
}
