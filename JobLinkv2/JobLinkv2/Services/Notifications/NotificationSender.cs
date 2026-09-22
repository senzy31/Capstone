using Dapper;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.Notifications
{
    // The types this app creates today. Must stay in sync with the Notifications.type CHECK
    // constraint in Database/schema-changes.sql - each one is a distinct row there.
    public static class NotificationTypes
    {
        public const string NewApplication = "NewApplication";
        public const string ApplicationStatusChanged = "ApplicationStatusChanged";
        public const string PremiumActivated = "PremiumActivated";
        public const string PremiumExpiringSoon = "PremiumExpiringSoon";
        public const string PremiumExpired = "PremiumExpired";
        public const string ConfirmExternalReminder = "ConfirmExternalReminder";
        public const string JobPublished = "JobPublished";
        public const string JobExpiringSoon = "JobExpiringSoon";
        public const string JobExpired = "JobExpired";
        public const string PurchaseConfirmed = "PurchaseConfirmed";
    }

    // Creates a notification for a user. The one thing every part of the app that needs to tell
    // someone something depends on, instead of each feature owning its own INSERT - deliberately
    // narrow (this is all a notification is) so it can be injected anywhere without pulling in an
    // unrelated store's whole surface (the apply flow, subscriptions, employer job posting, ...).
    public interface INotificationSender
    {
        void Send(int userId, string type, string message, string? link = null);

        // Whether a notification of this type (and, when given, this exact link - one job, one
        // application) has already been sent to this user at or after `sinceUtc`. The idempotency
        // check every "checked at request time, no background job" trigger uses so it fires once
        // per cycle (a Premium period, a job's time on the board, a single application) rather
        // than once per request. `link` tells apart two entities of the same type for the same
        // user (two jobs both nearing expiry); omit it for a user-wide singleton cycle (Premium).
        // Deleted notifications still count - dismissing one they got must not make it fire again.
        bool AlreadySent(int userId, string type, DateTime sinceUtc, string? link = null);
    }

    public sealed class SqlNotificationSender : INotificationSender
    {
        private readonly string _connectionString;

        public SqlNotificationSender(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        public void Send(int userId, string type, string message, string? link = null)
        {
            using var db = Open();

            db.Execute(@"
                INSERT INTO Notifications (user_id, message, is_read, created_at, is_deleted, type, link)
                VALUES (@userId, @message, 0, GETUTCDATE(), 0, @type, @link)",
                new
                {
                    userId,
                    message = new DbString { Value = message, IsAnsi = false, Length = 4000 },
                    type = new DbString { Value = type, IsAnsi = true, Length = 40 },
                    link = new DbString { Value = link, IsAnsi = true, Length = 255 }
                });
        }

        public bool AlreadySent(int userId, string type, DateTime sinceUtc, string? link = null)
        {
            using var db = Open();

            return db.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Notifications
                 WHERE user_id = @userId AND type = @type AND created_at >= @sinceUtc
                   AND (@link IS NULL OR link = @link)",
                new
                {
                    userId,
                    type = new DbString { Value = type, IsAnsi = true, Length = 40 },
                    sinceUtc,
                    link = new DbString { Value = link, IsAnsi = true, Length = 255 }
                }) > 0;
        }
    }
}
