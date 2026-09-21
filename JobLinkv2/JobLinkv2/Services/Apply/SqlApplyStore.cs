using Dapper;
using JobLinkv2.Models;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.Apply
{
    public sealed class SqlApplyStore : IApplyStore
    {
        // SQL Server error numbers for a unique index / constraint violation.
        private const int UniqueIndexViolation = 2601;
        private const int UniqueConstraintViolation = 2627;

        private const string ListingColumns = @"
            job_id AS JobId, external_job_id AS ExternalJobId, title AS Title, company AS Company,
            location AS Location, description AS Description, source_api AS SourceApi,
            posted_date AS PostedDate, ISNULL(is_deleted, 0) AS IsDeleted, source AS Source,
            employer_id AS EmployerId, apply_url AS ApplyUrl, apply_is_direct AS ApplyIsDirect,
            publisher AS Publisher, apply_options AS ApplyOptions, is_expired AS IsExpired,
            latitude AS Latitude, longitude AS Longitude";

        private const string ApplicationColumns = @"
            application_id AS ApplicationId, ISNULL(user_id, 0) AS UserId, ISNULL(job_id, 0) AS JobId,
            resume_id AS ResumeId, status AS Status, applied_at AS AppliedAt,
            ISNULL(is_deleted, 0) AS IsDeleted, application_type AS ApplicationType,
            redirected_at AS RedirectedAt, confirmed_at AS ConfirmedAt";

        private readonly string _connectionString;

        public SqlApplyStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        // varchar columns: send ANSI parameters so SQL Server can use the indexes.
        private static DbString Ansi(string? value, int length) =>
            new() { Value = value, IsAnsi = true, Length = length };

        private static bool IsUniqueViolation(SqlException ex) =>
            ex.Number is UniqueIndexViolation or UniqueConstraintViolation;

        // ----- job listings -----------------------------------------------------

        public JoblistingModel? GetListing(int jobId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<JoblistingModel>(
                $"SELECT {ListingColumns} FROM Job_Listings WHERE job_id = @jobId AND is_deleted = 0",
                new { jobId });
        }

        public void MarkListingExpired(int jobId)
        {
            using var db = Open();

            db.Execute("UPDATE Job_Listings SET is_expired = 1 WHERE job_id = @jobId AND is_deleted = 0", new { jobId });
        }

        public int UpsertExternalListing(ImportedListing listing)
        {
            const string sql = @"
                UPDATE Job_Listings
                   SET title = @Title, company = @Company, location = @Location, description = @Description,
                       posted_date = @PostedDate, apply_url = @ApplyUrl, apply_is_direct = @ApplyIsDirect,
                       publisher = @Publisher, apply_options = @ApplyOptions,
                       latitude = @Latitude, longitude = @Longitude,
                       is_expired = CASE WHEN @HasUsableLink = 1 THEN 0 ELSE is_expired END
                 WHERE source_api = @SourceApi AND external_job_id = @ExternalJobId AND is_deleted = 0;

                IF @@ROWCOUNT = 0
                    INSERT INTO Job_Listings
                        (external_job_id, title, company, location, description, source_api, posted_date, is_deleted,
                         source, employer_id, apply_url, apply_is_direct, publisher, apply_options, is_expired,
                         latitude, longitude)
                    VALUES
                        (@ExternalJobId, @Title, @Company, @Location, @Description, @SourceApi, @PostedDate, 0,
                         'External', NULL, @ApplyUrl, @ApplyIsDirect, @Publisher, @ApplyOptions, 0,
                         @Latitude, @Longitude);

                SELECT job_id FROM Job_Listings
                 WHERE source_api = @SourceApi AND external_job_id = @ExternalJobId AND is_deleted = 0;";

            var parameters = new
            {
                ExternalJobId = Ansi(listing.ExternalJobId, 900),
                SourceApi = Ansi(ListingSources.JSearchApi, 100),
                listing.Title,
                listing.Company,
                listing.Location,
                listing.Description,
                listing.PostedDate,
                listing.ApplyUrl,
                listing.ApplyIsDirect,
                listing.Publisher,
                ApplyOptions = listing.ApplyOptionsJson,
                listing.Latitude,
                listing.Longitude,
                HasUsableLink = ApplyLinkSelector.Choose(listing.ApplyUrl, listing.ApplyIsDirect, listing.ApplyOptionsJson) != null
            };

            using var db = Open();

            try
            {
                return db.QuerySingle<int>(sql, parameters);
            }
            catch (SqlException ex) when (IsUniqueViolation(ex))
            {
                // Two searches imported the same new job at once - the second one updates it.
                return db.QuerySingle<int>(sql, parameters);
            }
        }

        public JoblistingModel AddManualListing(JoblistingModel listing)
        {
            const string sql = @"
                INSERT INTO Job_Listings
                    (external_job_id, title, company, location, description, source_api, posted_date, is_deleted,
                     source, employer_id, apply_url, apply_is_direct, publisher, apply_options, is_expired,
                     latitude, longitude)
                VALUES
                    (@ExternalJobId, @Title, @Company, @Location, NULL, @SourceApi, NULL, 0,
                     'External', NULL, NULL, 0, NULL, NULL, 0, NULL, NULL);
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            using var db = Open();

            var id = db.QuerySingle<int>(sql, new
            {
                ExternalJobId = Ansi(listing.ExternalJobId, 900),
                SourceApi = Ansi(listing.SourceApi, 100),
                listing.Title,
                listing.Company,
                listing.Location
            });

            return db.QuerySingle<JoblistingModel>(
                $"SELECT {ListingColumns} FROM Job_Listings WHERE job_id = @id", new { id });
        }

        // ----- applications -----------------------------------------------------

        public ApplicationModel? GetApplication(int applicationId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<ApplicationModel>(
                $"SELECT {ApplicationColumns} FROM Applications WHERE application_id = @applicationId AND is_deleted = 0",
                new { applicationId });
        }

        public IReadOnlyList<ApplicationModel> GetApplicationsByUser(int userId)
        {
            using var db = Open();

            return db.Query<ApplicationModel>(
                $"SELECT {ApplicationColumns} FROM Applications WHERE user_id = @userId AND is_deleted = 0 ORDER BY application_id",
                new { userId }).ToList();
        }

        public ApplicationModel? FindActiveApplication(int userId, int jobId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<ApplicationModel>(
                $"SELECT {ApplicationColumns} FROM Applications WHERE user_id = @userId AND job_id = @jobId AND is_deleted = 0",
                new { userId, jobId });
        }

        public int AddApplication(ApplicationModel application)
        {
            const string sql = @"
                INSERT INTO Applications
                    (user_id, job_id, resume_id, status, applied_at, is_deleted, application_type, redirected_at, confirmed_at)
                VALUES
                    (@UserId, @JobId, @ResumeId, @Status, @AppliedAt, 0, @ApplicationType, @RedirectedAt, @ConfirmedAt);
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            using var db = Open();

            try
            {
                return db.QuerySingle<int>(sql, new
                {
                    application.UserId,
                    application.JobId,
                    application.ResumeId,
                    Status = Ansi(application.Status, 50),
                    application.AppliedAt,
                    ApplicationType = Ansi(application.ApplicationType, 10),
                    application.RedirectedAt,
                    application.ConfirmedAt
                });
            }
            catch (SqlException ex) when (IsUniqueViolation(ex))
            {
                throw new DuplicateApplicationException();
            }
        }

        public int? TryAddInternalApplication(ApplicationModel application, int limit, DateTime windowStartUtc)
        {
            // The count and the insert share one transaction, and the count takes a
            // range lock on this user's rows (UPDLOCK + HOLDLOCK), so two requests
            // from the same user can't both see "19 so far" and both insert.
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DECLARE @recent int = (
                    SELECT COUNT(*) FROM Applications WITH (UPDLOCK, HOLDLOCK)
                     WHERE user_id = @UserId AND application_type = 'Internal' AND applied_at >= @WindowStart);

                IF @recent >= @Limit
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT CAST(NULL AS int) AS Id;
                    RETURN;
                END

                INSERT INTO Applications
                    (user_id, job_id, resume_id, status, applied_at, is_deleted, application_type, redirected_at, confirmed_at)
                VALUES
                    (@UserId, @JobId, @ResumeId, @Status, @AppliedAt, 0, 'Internal', NULL, NULL);

                DECLARE @id int = CAST(SCOPE_IDENTITY() AS int);
                COMMIT TRANSACTION;
                SELECT @id AS Id;";

            using var db = Open();

            try
            {
                return db.QuerySingle<int?>(sql, new
                {
                    application.UserId,
                    application.JobId,
                    application.ResumeId,
                    Status = Ansi(application.Status, 50),
                    application.AppliedAt,
                    Limit = limit,
                    WindowStart = windowStartUtc
                });
            }
            catch (SqlException ex) when (IsUniqueViolation(ex))
            {
                throw new DuplicateApplicationException();
            }
        }

        public DateTime? GetOldestInternalApplicationSince(int userId, DateTime windowStartUtc)
        {
            using var db = Open();

            return db.QuerySingle<DateTime?>(
                @"SELECT MIN(applied_at) FROM Applications
                   WHERE user_id = @userId AND application_type = 'Internal' AND applied_at >= @windowStartUtc",
                new { userId, windowStartUtc });
        }

        public void UpdateApplication(ApplicationModel application)
        {
            using var db = Open();

            db.Execute(
                @"UPDATE Applications
                     SET status = @Status, applied_at = @AppliedAt, redirected_at = @RedirectedAt,
                         confirmed_at = @ConfirmedAt, resume_id = @ResumeId
                   WHERE application_id = @ApplicationId AND is_deleted = 0",
                new
                {
                    Status = Ansi(application.Status, 50),
                    application.AppliedAt,
                    application.RedirectedAt,
                    application.ConfirmedAt,
                    application.ResumeId,
                    application.ApplicationId
                });
        }

        public bool SoftDeleteApplication(int applicationId)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Applications SET is_deleted = 1 WHERE application_id = @applicationId AND is_deleted = 0",
                new { applicationId }) == 1;
        }

        // ----- people -----------------------------------------------------------

        public int? GetPrimaryResumeId(int userId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<int?>(
                "SELECT TOP 1 resume_id FROM Resumes WHERE user_id = @userId AND is_deleted = 0 ORDER BY resume_id",
                new { userId });
        }

        public string? GetUserName(int userId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<string>(
                "SELECT full_name FROM Users WHERE user_id = @userId AND is_deleted = 0",
                new { userId });
        }

        public void AddNotification(int userId, string message)
        {
            using var db = Open();

            db.Execute(
                @"INSERT INTO Notifications (user_id, message, is_read, created_at, is_deleted)
                  VALUES (@userId, @message, 0, @createdAt, 0)",
                new { userId, message, createdAt = DateTime.UtcNow });
        }
    }
}
