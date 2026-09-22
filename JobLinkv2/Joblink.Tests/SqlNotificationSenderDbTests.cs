using Dapper;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Notifications;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // SqlNotificationSender against the real Joblinkv2 database: the row it writes, the CHECK
    // constraint on type, unicode text, and AlreadySent's link-scoping and time window - the
    // idempotency every "checked at request time, no background job" trigger depends on. Every
    // test makes its own user and removes it (and its notifications) afterwards.
    public sealed class SqlNotificationSenderDbTests : IDisposable
    {
        private readonly SqlNotificationSender _sender = new(DbConfig.DefaultConnectionString);
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

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Notifications WHERE user_id IN @users;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users });
        }

        [DbFact]
        public void Send_writes_the_type_link_and_message_including_unicode()
        {
            var user = NewUser();

            _sender.Send(user, NotificationTypes.PremiumActivated, "María Ñ's Premium plan is now active.", "/DASHBOARD/Plans.html");

            using var db = Open();

            var row = db.QuerySingle<(string Message, string Type, string Link, bool IsRead)>(
                "SELECT message AS Message, type AS Type, link AS Link, CAST(is_read AS bit) AS IsRead FROM Notifications WHERE user_id = @user",
                new { user });

            Assert.Equal("María Ñ's Premium plan is now active.", row.Message);
            Assert.Equal(NotificationTypes.PremiumActivated, row.Type);
            Assert.Equal("/DASHBOARD/Plans.html", row.Link);
            Assert.False(row.IsRead);
        }

        [DbFact]
        public void Send_with_no_link_leaves_it_null()
        {
            var user = NewUser();

            _sender.Send(user, NotificationTypes.NewApplication, "Someone applied.");

            using var db = Open();

            Assert.Null(db.QuerySingle<string?>("SELECT link FROM Notifications WHERE user_id = @user", new { user }));
        }

        [DbFact]
        public void The_table_refuses_a_type_outside_the_known_list()
        {
            var user = NewUser();

            Assert.Throws<SqlException>(() => _sender.Send(user, "NotARealType", "..."));
        }

        [DbFact]
        public void AlreadySent_is_false_until_a_matching_notification_exists_since_the_given_time()
        {
            var user = NewUser();

            // A few seconds of slack against created_at (server clock, DATETIME's ~3ms rounding)
            // vs. this process's own clock - the point under test is "since a while ago" vs.
            // "since a while from now", not a race against the exact insert instant.
            var before = DateTime.UtcNow.AddSeconds(-5);

            Assert.False(_sender.AlreadySent(user, NotificationTypes.PremiumExpiringSoon, before));

            _sender.Send(user, NotificationTypes.PremiumExpiringSoon, "Expiring soon.");

            Assert.True(_sender.AlreadySent(user, NotificationTypes.PremiumExpiringSoon, before));
            Assert.False(_sender.AlreadySent(user, NotificationTypes.PremiumExpiringSoon, DateTime.UtcNow.AddMinutes(1)));  // sent before this later window started
        }

        [DbFact]
        public void AlreadySent_does_not_confuse_a_different_type_or_a_different_users_notification()
        {
            var user = NewUser();
            var other = NewUser();
            var since = DateTime.UtcNow.AddMinutes(-1);

            _sender.Send(user, NotificationTypes.JobPublished, "Published.");

            Assert.False(_sender.AlreadySent(user, NotificationTypes.JobExpired, since));       // different type
            Assert.False(_sender.AlreadySent(other, NotificationTypes.JobPublished, since));    // different user
            Assert.True(_sender.AlreadySent(user, NotificationTypes.JobPublished, since));
        }

        [DbFact]
        public void AlreadySent_with_a_link_only_matches_that_exact_link_so_two_jobs_dont_share_one_cycle()
        {
            var user = NewUser();
            var since = DateTime.UtcNow.AddMinutes(-1);

            _sender.Send(user, NotificationTypes.JobExpiringSoon, "Job A expiring.", "/Employer Dashboard/dashboard.html?job=1");

            Assert.True(_sender.AlreadySent(user, NotificationTypes.JobExpiringSoon, since, "/Employer Dashboard/dashboard.html?job=1"));
            Assert.False(_sender.AlreadySent(user, NotificationTypes.JobExpiringSoon, since, "/Employer Dashboard/dashboard.html?job=2"));

            // Omitting the link checks across every link for that type/user (the user-wide, no-entity case).
            Assert.True(_sender.AlreadySent(user, NotificationTypes.JobExpiringSoon, since));
        }

        [DbFact]
        public void A_deleted_notification_still_counts_for_AlreadySent()
        {
            var user = NewUser();
            var since = DateTime.UtcNow.AddMinutes(-1);

            _sender.Send(user, NotificationTypes.PremiumExpired, "Expired.");

            using (var db = Open())
                db.Execute("UPDATE Notifications SET is_deleted = 1 WHERE user_id = @user", new { user });

            Assert.True(_sender.AlreadySent(user, NotificationTypes.PremiumExpired, since));
        }
    }
}
