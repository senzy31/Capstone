using Dapper;
using JobLinkv2.Models;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.MyData
{
    public enum SaveJobOutcome
    {
        Saved,
        JobNotFound
    }

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
            ISNULL(is_read, 0) AS IsRead, created_at AS CreatedAt, ISNULL(is_deleted, 0) AS IsDeleted";

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

        // The only thing a user may change about a notification.
        public bool SetNotificationRead(int userId, int notificationId, bool isRead)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Notifications SET is_read = @isRead WHERE notification_id = @notificationId AND user_id = @userId AND is_deleted = 0",
                new { isRead, notificationId, userId }) == 1;
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

        // Saves a job for the caller - or brings back one they un-saved. Saving what
        // is already saved is fine. The job has to exist.
        public SaveJobOutcome SaveJob(int userId, int jobId)
        {
            using var db = Open();

            if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM Job_Listings WHERE job_id = @jobId AND is_deleted = 0", new { jobId }) != 1)
                return SaveJobOutcome.JobNotFound;

            try
            {
                db.Execute(@"
                    UPDATE Saved_Jobs SET is_deleted = 0 WHERE user_id = @userId AND job_id = @jobId;

                    IF @@ROWCOUNT = 0
                        INSERT INTO Saved_Jobs (user_id, job_id, is_deleted) VALUES (@userId, @jobId, 0);",
                    new { userId, jobId });
            }
            catch (SqlException ex) when (ex.Number is UniqueIndexViolation or UniqueConstraintViolation)
            {
                // two requests saved the same job at once - it is saved either way
            }

            return SaveJobOutcome.Saved;
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
