using Dapper;
using JobLinkv2.Models;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.MyData
{
    public enum SaveJobOutcome
    {
        Saved,
        JobNotFound,
        LimitReached
    }

    public sealed record NotificationPage(IReadOnlyList<NotificationModel> Items, int TotalCount, int UnreadCount);

    // The things that belong to one user and aren't part of a resume: their
    // notifications, the jobs they saved, and their job matches.
    //
    // Every method takes the caller's user id and puts it in the SQL, so a row that
    // isn't theirs can't be read, changed or deleted. Soft-deleted rows (is_deleted = 1)
    // are invisible. Notifications and matches are created by the server (the apply
    // flow, the matching service), so there is deliberately no "add" for them here.
    public sealed class UserDataStore
    {
        // SQL Server error numbers for a unique index / constraint violation.
        private const int UniqueIndexViolation = 2601;
        private const int UniqueConstraintViolation = 2627;

        private const string NotificationColumns = @"
            notification_id AS NotificationId, user_id AS UserId, message AS Message,
            ISNULL(is_read, 0) AS IsRead, created_at AS CreatedAt, ISNULL(is_deleted, 0) AS IsDeleted,
            type AS Type, link AS Link";

        private const string SavedJobColumns = @"
            user_id AS UserId, job_id AS JobId, ISNULL(is_deleted, 0) AS IsDeleted";

        private const string MatchColumns = @"
            match_id AS MatchId, user_id AS UserId, job_id AS JobId, match_score AS MatchScore,
            created_at AS CreatedAt, ISNULL(is_deleted, 0) AS IsDeleted";

        private readonly string _connectionString;

        public UserDataStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        // ----- notifications ----------------------------------------------------

        // Newest first.
        public IReadOnlyList<NotificationModel> ListNotifications(int userId)
        {
            using var db = Open();

            return db.Query<NotificationModel>(
                $"SELECT {NotificationColumns} FROM Notifications WHERE user_id = @userId AND is_deleted = 0 ORDER BY created_at DESC, notification_id DESC",
                new { userId }).ToList();
        }

        public NotificationModel? GetNotification(int userId, int notificationId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<NotificationModel>(
                $"SELECT {NotificationColumns} FROM Notifications WHERE notification_id = @notificationId AND user_id = @userId AND is_deleted = 0",
                new { notificationId, userId });
        }

        // Newest first, one page at a time, with the total count (for pagination) and how many
        // are unread (shown on the same request the bell dropdown already makes).
        public NotificationPage ListNotificationsPage(int userId, int page, int pageSize)
        {
            using var db = Open();

            var offset = (Math.Max(page, 1) - 1) * pageSize;

            using var multi = db.QueryMultiple($@"
                SELECT {NotificationColumns} FROM Notifications
                 WHERE user_id = @userId AND is_deleted = 0
                 ORDER BY created_at DESC, notification_id DESC
                 OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

                SELECT COUNT(*) FROM Notifications WHERE user_id = @userId AND is_deleted = 0;

                SELECT COUNT(*) FROM Notifications WHERE user_id = @userId AND is_deleted = 0 AND ISNULL(is_read, 0) = 0;",
                new { userId, offset, pageSize });

            var items = multi.Read<NotificationModel>().ToList();
            var totalCount = multi.ReadSingle<int>();
            var unreadCount = multi.ReadSingle<int>();

            return new NotificationPage(items, totalCount, unreadCount);
        }

        public int UnreadNotificationCount(int userId)
        {
            using var db = Open();

            return db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Notifications WHERE user_id = @userId AND is_deleted = 0 AND ISNULL(is_read, 0) = 0",
                new { userId });
        }

        // The only thing a user may change about a notification.
        public bool SetNotificationRead(int userId, int notificationId, bool isRead)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Notifications SET is_read = @isRead WHERE notification_id = @notificationId AND user_id = @userId AND is_deleted = 0",
                new { isRead, notificationId, userId }) == 1;
        }

        // Returns how many were actually flipped (already-read ones don't count).
        public int MarkAllNotificationsRead(int userId)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Notifications SET is_read = 1 WHERE user_id = @userId AND is_deleted = 0 AND ISNULL(is_read, 0) = 0",
                new { userId });
        }

        public bool DeleteNotification(int userId, int notificationId)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Notifications SET is_deleted = 1 WHERE notification_id = @notificationId AND user_id = @userId AND is_deleted = 0",
                new { notificationId, userId }) == 1;
        }

        // ----- saved jobs -------------------------------------------------------

        public IReadOnlyList<SavedJobsModel> ListSavedJobs(int userId)
        {
            using var db = Open();

            return db.Query<SavedJobsModel>(
                $"SELECT {SavedJobColumns} FROM Saved_Jobs WHERE user_id = @userId AND is_deleted = 0 ORDER BY job_id",
                new { userId }).ToList();
        }

        public int CountSavedJobs(int userId)
        {
            using var db = Open();

            return db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @userId AND is_deleted = 0", new { userId });
        }

        // Saves a job for the caller - or brings back one they un-saved. Saving what is
        // already saved is fine (and never counts against a limit). The job has to exist.
        //
        // maxLive is the plan's cap on live saved jobs (null = no cap). Counting and saving
        // are one step under a per-user lock, so parallel requests can't both take the last
        // slot. Saved jobs they already have above the cap are left alone.
        public SaveJobOutcome SaveJob(int userId, int jobId, int? maxLive = null)
        {
            using var db = Open();

            var code = db.QuerySingle<int>($@"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {SqlLocks.Take}

                IF NOT EXISTS (SELECT 1 FROM Job_Listings WHERE job_id = @jobId AND is_deleted = 0)
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 1;   -- the job doesn't exist
                    RETURN;
                END

                IF EXISTS (SELECT 1 FROM Saved_Jobs WHERE user_id = @userId AND job_id = @jobId AND is_deleted = 0)
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 0;   -- already saved
                    RETURN;
                END

                IF @maxLive IS NOT NULL
                   AND (SELECT COUNT(*) FROM Saved_Jobs WHERE user_id = @userId AND is_deleted = 0) >= @maxLive
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 2;   -- at the limit
                    RETURN;
                END

                UPDATE Saved_Jobs SET is_deleted = 0 WHERE user_id = @userId AND job_id = @jobId;

                IF @@ROWCOUNT = 0
                    INSERT INTO Saved_Jobs (user_id, job_id, is_deleted) VALUES (@userId, @jobId, 0);

                COMMIT TRANSACTION;
                SELECT 0;",
                new { LockName = SqlLocks.Name("savedjob", userId), userId, jobId, maxLive });

            return code switch
            {
                1 => SaveJobOutcome.JobNotFound,
                2 => SaveJobOutcome.LimitReached,
                _ => SaveJobOutcome.Saved
            };
        }

        public bool UnsaveJob(int userId, int jobId)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Saved_Jobs SET is_deleted = 1 WHERE user_id = @userId AND job_id = @jobId AND is_deleted = 0",
                new { userId, jobId }) == 1;
        }

        // ----- job matches ------------------------------------------------------

        public IReadOnlyList<JobMatchModel> ListMatches(int userId)
        {
            using var db = Open();

            return db.Query<JobMatchModel>(
                $"SELECT {MatchColumns} FROM Job_Match WHERE user_id = @userId AND is_deleted = 0 ORDER BY match_score DESC, match_id",
                new { userId }).ToList();
        }

        public JobMatchModel? GetMatch(int userId, int matchId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<JobMatchModel>(
                $"SELECT {MatchColumns} FROM Job_Match WHERE match_id = @matchId AND user_id = @userId AND is_deleted = 0",
                new { matchId, userId });
        }
    }
}
