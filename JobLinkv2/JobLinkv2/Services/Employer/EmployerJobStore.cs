using Dapper;
using JobLinkv2.Models;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.Employer
{
    public enum PublishOutcome { Ok, NoCredit, NotFound }

    public enum RenewOutcome { Ok, NoCredit, NotFound }

    public sealed record JobPurchaseRecord(
        int PurchaseId, int EmployerId, string Package, string CreditKind,
        decimal AmountPhp, int CreditsGranted, int CreditsRemaining, DateTime PurchasedAt);

    public sealed record EmployerJobSummary(JoblistingModel Job, int ApplicantCount);

    // Everything employer job posting needs from the database: purchases/credits, the jobs
    // themselves, and their required-skills join. A concrete class (not interface+fake), like
    // ResumeDataStore/UserDataStore - its tests run against a real database via [DbFact].
    public sealed class EmployerJobStore
    {
        private const string ListingColumns = @"
            job_id AS JobId, external_job_id AS ExternalJobId, title AS Title, company AS Company,
            location AS Location, description AS Description, source_api AS SourceApi,
            posted_date AS PostedDate, ISNULL(is_deleted, 0) AS IsDeleted, source AS Source,
            employer_id AS EmployerId, apply_url AS ApplyUrl, apply_is_direct AS ApplyIsDirect,
            publisher AS Publisher, apply_options AS ApplyOptions, is_expired AS IsExpired,
            latitude AS Latitude, longitude AS Longitude, ISNULL(status, 'Draft') AS Status,
            salary_min AS SalaryMin, salary_max AS SalaryMax, work_setup AS WorkSetup,
            job_type AS JobType, published_at AS PublishedAt, expires_at AS ExpiresAt";

        private readonly string _connectionString;

        public EmployerJobStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static DbString Ansi(string? value, int length) =>
            new() { Value = value, IsAnsi = true, Length = length };

        // ----- purchases / credits ------------------------------------------------

        public JobPurchaseRecord RecordPurchase(int employerId, PostingPackageOption option, DateTime now)
        {
            using var db = Open();

            const string sql = @"
                INSERT INTO Job_Post_Purchases
                    (employer_id, package, credit_kind, amount_php, credits_granted, credits_remaining, purchased_at)
                VALUES
                    (@EmployerId, @Package, @CreditKind, @AmountPhp, @Credits, @Credits, @Now);
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            var id = db.QuerySingle<int>(sql, new
            {
                EmployerId = employerId,
                Package = Ansi(option.Package, 10),
                CreditKind = Ansi(option.CreditKind, 10),
                AmountPhp = option.PricePhp,
                option.Credits,
                Now = now
            });

            return db.QuerySingle<JobPurchaseRecord>(@"
                SELECT purchase_id AS PurchaseId, employer_id AS EmployerId, package AS Package,
                       credit_kind AS CreditKind, amount_php AS AmountPhp, credits_granted AS CreditsGranted,
                       credits_remaining AS CreditsRemaining, purchased_at AS PurchasedAt
                  FROM Job_Post_Purchases WHERE purchase_id = @id", new { id });
        }

        public int CreditBalance(int employerId, string creditKind)
        {
            using var db = Open();

            return db.ExecuteScalar<int>(
                "SELECT ISNULL(SUM(credits_remaining), 0) FROM Job_Post_Purchases WHERE employer_id = @employerId AND credit_kind = @creditKind",
                new { employerId, creditKind = Ansi(creditKind, 10) });
        }

        // Spends the oldest available credit of this kind for this employer. Returns false if
        // there wasn't one - never goes negative, two parallel spends can't both take the last one.
        // The lock check is read and thrown on in C# (not via SqlLocks.Take's own T-SQL ROLLBACK)
        // because the ambient transaction here is an ADO.NET SqlTransaction: letting the lock
        // failure roll it back a second time on `using`'s Dispose would throw "transaction has
        // completed". Same shape as SqlSubscriptionStore.ChangeOnce.
        private static bool TrySpendCredit(SqlConnection db, SqlTransaction transaction, int employerId, string creditKind)
        {
            var lockResult = db.ExecuteScalar<int>(@"
                DECLARE @lock int;
                EXEC @lock = sp_getapplock @Resource = @LockName, @LockMode = 'Exclusive',
                                           @LockOwner = 'Transaction', @LockTimeout = 15000;
                SELECT @lock;",
                new { LockName = SqlLocks.Name("jobcredit", employerId) }, transaction);

            if (lockResult < 0)
                throw new InvalidOperationException("Could not get the job posting credit lock for this employer in time.");

            var purchaseId = db.ExecuteScalar<int?>(@"
                SELECT TOP 1 purchase_id FROM Job_Post_Purchases
                 WHERE employer_id = @employerId AND credit_kind = @creditKind AND credits_remaining > 0
                 ORDER BY purchased_at, purchase_id",
                new { employerId, creditKind = Ansi(creditKind, 10) }, transaction);

            if (purchaseId is null)
                return false;

            db.Execute(
                "UPDATE Job_Post_Purchases SET credits_remaining = credits_remaining - 1 WHERE purchase_id = @purchaseId",
                new { purchaseId }, transaction);

            return true;
        }

        // ----- jobs -----------------------------------------------------------------

        public JoblistingModel CreateDraft(JoblistingModel job)
        {
            using var db = Open();

            const string sql = @"
                INSERT INTO Job_Listings
                    (title, company, location, description, is_deleted, source, employer_id,
                     apply_is_direct, is_expired, status, salary_min, salary_max, work_setup, job_type)
                VALUES
                    (@Title, @Company, @Location, @Description, 0, 'Internal', @EmployerId,
                     0, 0, 'Draft', @SalaryMin, @SalaryMax, @WorkSetup, @JobType);
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            var id = db.QuerySingle<int>(sql, new
            {
                job.Title,
                job.Company,
                job.Location,
                job.Description,
                job.EmployerId,
                job.SalaryMin,
                job.SalaryMax,
                WorkSetup = Ansi(job.WorkSetup, 20),
                JobType = Ansi(job.JobType, 20)
            });

            return db.QuerySingle<JoblistingModel>($"SELECT {ListingColumns} FROM Job_Listings WHERE job_id = @id", new { id });
        }

        public JoblistingModel? GetOwnJob(int employerId, int jobId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<JoblistingModel>(
                $"SELECT {ListingColumns} FROM Job_Listings WHERE job_id = @jobId AND employer_id = @employerId AND source = 'Internal' AND is_deleted = 0",
                new { jobId, employerId });
        }

        public int GetApplicantCount(int jobId)
        {
            using var db = Open();

            return db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Applications WHERE job_id = @jobId AND application_type = 'Internal' AND is_deleted = 0",
                new { jobId });
        }

        // Set once, best-effort, right after Publish geocodes the job's location.
        public void SetCoordinates(int jobId, decimal latitude, decimal longitude)
        {
            using var db = Open();

            db.Execute(
                "UPDATE Job_Listings SET latitude = @latitude, longitude = @longitude WHERE job_id = @jobId",
                new { jobId, latitude, longitude });
        }

        // Edits title/company/location/description/salary/work setup/job type only - status,
        // dates and credits change only through Publish/Close/Renew. Returns false if the job
        // doesn't exist, isn't Internal, or isn't this employer's.
        public bool UpdateOwnJob(int employerId, int jobId, string title, string company, string description,
            string location, decimal? salaryMin, decimal? salaryMax, string? workSetup, string? jobType)
        {
            using var db = Open();

            var rows = db.Execute(@"
                UPDATE Job_Listings
                   SET title = @Title, company = @Company, description = @Description, location = @Location,
                       salary_min = @SalaryMin, salary_max = @SalaryMax, work_setup = @WorkSetup, job_type = @JobType
                 WHERE job_id = @JobId AND employer_id = @EmployerId AND source = 'Internal' AND is_deleted = 0",
                new
                {
                    JobId = jobId,
                    EmployerId = employerId,
                    Title = title,
                    Company = company,
                    Description = description,
                    Location = location,
                    SalaryMin = salaryMin,
                    SalaryMax = salaryMax,
                    WorkSetup = Ansi(workSetup, 20),
                    JobType = Ansi(jobType, 20)
                });

            return rows > 0;
        }

        // Spends one Post credit and makes the job Active for the next 30 days.
        public PublishOutcome Publish(int employerId, int jobId, DateTime now)
        {
            using var db = Open();
            using var transaction = db.BeginTransaction();

            var owned = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Job_Listings WHERE job_id = @jobId AND employer_id = @employerId AND source = 'Internal' AND is_deleted = 0",
                new { jobId, employerId }, transaction);

            if (owned == 0)
            {
                transaction.Rollback();
                return PublishOutcome.NotFound;
            }

            if (!TrySpendCredit(db, transaction, employerId, JobCreditKinds.Post))
            {
                transaction.Rollback();
                return PublishOutcome.NoCredit;
            }

            var expiresAt = now.AddDays(JobPostCatalogue.ActiveDays);

            db.Execute(@"
                UPDATE Job_Listings
                   SET status = 'Active', published_at = @Now, expires_at = @ExpiresAt, is_expired = 0
                 WHERE job_id = @JobId",
                new { JobId = jobId, Now = now, ExpiresAt = expiresAt }, transaction);

            transaction.Commit();
            return PublishOutcome.Ok;
        }

        public bool Close(int employerId, int jobId)
        {
            using var db = Open();

            var rows = db.Execute(
                "UPDATE Job_Listings SET status = 'Closed' WHERE job_id = @jobId AND employer_id = @employerId AND source = 'Internal' AND is_deleted = 0",
                new { jobId, employerId });

            return rows > 0;
        }

        // Spends one Renewal credit and extends the job by 30 days from whichever is later:
        // now, or its current expiry (so renewing early doesn't waste the days left). Revives
        // a Closed or Expired job back to Active.
        public RenewOutcome Renew(int employerId, int jobId, DateTime now)
        {
            using var db = Open();
            using var transaction = db.BeginTransaction();

            var found = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Job_Listings WHERE job_id = @jobId AND employer_id = @employerId AND source = 'Internal' AND is_deleted = 0",
                new { jobId, employerId }, transaction);

            if (found == 0)
            {
                transaction.Rollback();
                return RenewOutcome.NotFound;
            }

            var currentExpiresAt = db.ExecuteScalar<DateTime?>(
                "SELECT expires_at FROM Job_Listings WHERE job_id = @jobId", new { jobId }, transaction);

            if (!TrySpendCredit(db, transaction, employerId, JobCreditKinds.Renewal))
            {
                transaction.Rollback();
                return RenewOutcome.NoCredit;
            }

            var baseline = currentExpiresAt is { } exp && exp > now ? exp : now;
            var expiresAt = baseline.AddDays(JobPostCatalogue.ActiveDays);

            db.Execute(@"
                UPDATE Job_Listings
                   SET status = 'Active', expires_at = @ExpiresAt, is_expired = 0,
                       published_at = ISNULL(published_at, @Now)
                 WHERE job_id = @JobId",
                new { JobId = jobId, ExpiresAt = expiresAt, Now = now }, transaction);

            transaction.Commit();
            return RenewOutcome.Ok;
        }

        // This employer's own jobs, newest first, with each one's applicant count. Rows that
        // say Active but are past their own expires_at are flipped to Expired first - the
        // lazy write "no background job" relies on (checked whenever the employer looks at
        // their own jobs, not on a schedule).
        public IReadOnlyList<EmployerJobSummary> ListOwnJobs(int employerId, DateTime now)
        {
            using var db = Open();

            db.Execute(
                "UPDATE Job_Listings SET status = 'Expired' WHERE employer_id = @employerId AND source = 'Internal' AND status = 'Active' AND expires_at IS NOT NULL AND expires_at <= @now AND is_deleted = 0",
                new { employerId, now });

            var jobs = db.Query<JoblistingModel>(
                $"SELECT {ListingColumns} FROM Job_Listings WHERE employer_id = @employerId AND source = 'Internal' AND is_deleted = 0 ORDER BY job_id DESC",
                new { employerId }).ToList();

            if (jobs.Count == 0)
                return Array.Empty<EmployerJobSummary>();

            var jobIds = jobs.Select(j => j.JobId).ToList();

            var counts = db.Query<ApplicantCountRow>(@"
                SELECT job_id AS JobId, COUNT(*) AS Count FROM Applications
                 WHERE job_id IN @jobIds AND application_type = 'Internal' AND is_deleted = 0
                 GROUP BY job_id",
                new { jobIds }).ToDictionary(r => r.JobId, r => r.Count);

            return jobs.Select(j => new EmployerJobSummary(j, counts.GetValueOrDefault(j.JobId))).ToList();
        }

        private sealed class ApplicantCountRow
        {
            public int JobId { get; set; }
            public int Count { get; set; }
        }

        // Active internal jobs whose expiry hasn't passed - for job seeker search/recommendations.
        // Never writes (a stale Active row here just gets filtered out, not flipped - only the
        // employer's own request in ListOwnJobs does that).
        public IReadOnlyList<JoblistingModel> ListActiveInternalJobs(DateTime now)
        {
            using var db = Open();

            return db.Query<JoblistingModel>(
                $"SELECT {ListingColumns} FROM Job_Listings WHERE source = 'Internal' AND status = 'Active' AND (expires_at IS NULL OR expires_at > @now) AND is_deleted = 0",
                new { now }).ToList();
        }

        // ----- required skills (shared Skills catalogue, like resumes) --------------

        public IReadOnlyList<SkillsModel> GetSkills(int jobId)
        {
            using var db = Open();

            return db.Query<SkillsModel>(@"
                SELECT s.skill_id AS SkillId, s.skill_name AS SkillName, 0 AS IsDeleted
                  FROM Job_Listing_Skills jls
                  JOIN Skills s ON s.skill_id = jls.skill_id
                 WHERE jls.job_id = @jobId AND jls.is_deleted = 0 AND ISNULL(s.is_deleted, 0) = 0
                 ORDER BY s.skill_id", new { jobId }).ToList();
        }

        // Replaces the job's required skills with exactly this set: anything not in `skillIds`
        // is soft-deleted, anything in it is inserted or un-deleted. A no-op id (-1, never a
        // real skill_id) stands in for "none" so an empty list doesn't need special-casing SQL.
        public void SetSkills(int jobId, IReadOnlyList<int> skillIds)
        {
            using var db = Open();
            using var transaction = db.BeginTransaction();

            var keep = skillIds.Count > 0 ? skillIds : new[] { -1 };

            db.Execute(
                "UPDATE Job_Listing_Skills SET is_deleted = 1 WHERE job_id = @jobId AND skill_id NOT IN @keep",
                new { jobId, keep }, transaction);

            foreach (var skillId in skillIds)
            {
                db.Execute(@"
                    UPDATE Job_Listing_Skills SET is_deleted = 0 WHERE job_id = @jobId AND skill_id = @skillId;
                    IF @@ROWCOUNT = 0
                        INSERT INTO Job_Listing_Skills (job_id, skill_id, is_deleted) VALUES (@jobId, @skillId, 0);",
                    new { jobId, skillId }, transaction);
            }

            transaction.Commit();
        }
    }
}
