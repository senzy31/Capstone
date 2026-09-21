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
