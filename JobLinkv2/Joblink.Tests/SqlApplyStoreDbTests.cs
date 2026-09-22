using Dapper;
using JobLinkv2.Models;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Apply;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // Runs a test only when JOBLINK_TEST_DB=1, so a plain `dotnet test` never touches a database.
    public sealed class DbFactAttribute : FactAttribute
    {
        public DbFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("JOBLINK_TEST_DB") != "1")
                Skip = "Needs the local Joblinkv2 database. Run with JOBLINK_TEST_DB=1 (see tests/README.md).";
        }
    }

    // The real SqlApplyStore against the real Joblinkv2 database - the SQL the
    // in-memory fake can't check: the import upsert, the unique indexes, the
    // atomic apply limit and the table constraints. Every test creates its own
    // users and jobs and deletes them afterwards.
    public sealed class SqlApplyStoreDbTests : IDisposable
    {
        private readonly SqlApplyStore _store = new(DbConfig.DefaultConnectionString);
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private int NewUser(string role = "user")
        {
            using var db = Open();

            var id = db.QuerySingle<int>(
                @"INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted)
                  VALUES (@name, @email, 'x', @role, GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest {_tag}", email = $"dbtest.{_tag}.{_userIds.Count}@example.com", role });

            _userIds.Add(id);
            return id;
        }

        private int NewInternalJob(int employerId)
        {
            using var db = Open();

            return db.QuerySingle<int>(
                @"INSERT INTO Job_Listings (external_job_id, title, company, source_api, is_deleted, source, employer_id)
                  VALUES (@ext, 'DB test job', 'Acme', 'employer', 0, 'Internal', @employerId);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { ext = Guid.NewGuid().ToString("N"), employerId });
        }

        private ImportedListing Imported(string? id = null, string title = "Backend Developer", string? applyUrl = "https://www.linkedin.com/jobs/view/1") =>
            new(id ?? $"dbtest-{_tag}-{Guid.NewGuid():N}", title, "Globex", "Makati", "desc", new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc),
                applyUrl, false, "LinkedIn", "[{\"publisher\":\"LinkedIn\",\"apply_link\":\"https://www.linkedin.com/jobs/view/1\",\"is_direct\":false}]", 14.5m, 121.0m);

        private int Count(string sql, object? args = null)
        {
            using var db = Open();
            return db.QuerySingle<int>(sql, args);
        }

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? string.Join(",", _userIds) : "-1";

            db.Execute($@"
                DELETE FROM Notifications WHERE user_id IN ({users});
                DELETE FROM Applications WHERE user_id IN ({users});
                DELETE FROM Job_Listings WHERE external_job_id LIKE '%{_tag}%' OR employer_id IN ({users}) OR title LIKE '%{_tag}%';
                DELETE FROM Users WHERE user_id IN ({users});");
        }

        // ----- import upsert ----------------------------------------------------

        [DbFact]
        public void Importing_the_same_job_twice_updates_the_row_instead_of_adding_one()
        {
            var listing = Imported();

            var first = _store.UpsertExternalListing(listing);
            var second = _store.UpsertExternalListing(listing with { Title = "Backend Developer II", Publisher = "Indeed" });

            Assert.Equal(first, second);
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Job_Listings WHERE external_job_id = @id", new { id = listing.ExternalJobId }));

            var stored = _store.GetListing(first)!;
            Assert.Equal("Backend Developer II", stored.Title);
            Assert.Equal("Indeed", stored.Publisher);
            Assert.Equal("External", stored.Source);
            Assert.Null(stored.EmployerId);
            Assert.Equal("jsearch", stored.SourceApi);
        }

        [DbFact]
        public void A_re_import_with_a_working_link_clears_the_expired_flag_but_a_dead_link_does_not()
        {
            var listing = Imported();
            var id = _store.UpsertExternalListing(listing);

            _store.MarkListingExpired(id);
            Assert.True(_store.GetListing(id)!.IsExpired);

            _store.UpsertExternalListing(listing with { ApplyUrl = "https://www.linkedin.com/jobs/expired/1", ApplyOptionsJson = null });
            Assert.True(_store.GetListing(id)!.IsExpired);          // still dead

            _store.UpsertExternalListing(listing);                   // a working link is back
            Assert.False(_store.GetListing(id)!.IsExpired);
        }

        [DbFact]
        public void Parallel_imports_of_the_same_new_job_end_up_as_one_row()
        {
            var listing = Imported();

            var ids = Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _store.UpsertExternalListing(listing))))
                .GetAwaiter().GetResult();

            Assert.Single(ids.Distinct());
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Job_Listings WHERE external_job_id = @id", new { id = listing.ExternalJobId }));
        }

        [DbFact]
        public void A_400_character_job_id_and_unicode_text_are_stored_intact()
        {
            var longId = $"dbtest-{_tag}-" + new string('A', 400);
            var listing = Imported(longId, title: $"Développeur ₱ 日本語 {_tag}") with { Company = "Café Ñandú" };

            var id = _store.UpsertExternalListing(listing);
            var stored = _store.GetListing(id)!;

            Assert.Equal(longId, stored.ExternalJobId);
            Assert.Equal(listing.Title, stored.Title);
            Assert.Equal("Café Ñandú", stored.Company);
            Assert.Equal(14.5m, stored.Latitude);
            Assert.Equal(121.0m, stored.Longitude);
            Assert.Contains("\"is_direct\"", stored.ApplyOptions);
        }

        [DbFact]
        public void Soft_deleted_listings_are_not_returned()
        {
            var id = _store.UpsertExternalListing(Imported());

            using (var db = Open())
                db.Execute("UPDATE Job_Listings SET is_deleted = 1 WHERE job_id = @id", new { id });

            Assert.Null(_store.GetListing(id));
        }

        [DbFact]
        public void A_hand_added_job_is_saved_as_manual_External_with_no_apply_data()
        {
            var listing = _store.AddManualListing(new JoblistingModel
            {
                ExternalJobId = $"manual-{_tag}", Title = $"QA {_tag}", Company = "Acme", Location = "Davao", SourceApi = "manual"
            });

            Assert.True(listing.JobId > 0);
            Assert.Equal("External", listing.Source);
            Assert.Null(listing.EmployerId);
            Assert.Null(listing.ApplyUrl);
            Assert.False(listing.ApplyIsDirect);
            Assert.False(listing.IsExpired);
        }

        // ----- applications -----------------------------------------------------

        [DbFact]
        public void A_second_live_application_for_the_same_job_is_rejected_but_a_withdrawn_one_can_be_replaced()
        {
            var user = NewUser();
            var job = _store.UpsertExternalListing(Imported());
            var application = new ApplicationModel { UserId = user, JobId = job, Status = "Redirected", ApplicationType = "External", RedirectedAt = DateTime.UtcNow };

            var first = _store.AddApplication(application);

            Assert.Throws<DuplicateApplicationException>(() => _store.AddApplication(application));

            Assert.True(_store.SoftDeleteApplication(first));
            var replacement = _store.AddApplication(application);
            Assert.NotEqual(first, replacement);
        }

        [DbFact]
        public void Parallel_clicks_leave_exactly_one_live_application()
        {
            var user = NewUser();
            var job = _store.UpsertExternalListing(Imported());

            var outcomes = Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            {
                try
                {
                    return _store.AddApplication(new ApplicationModel { UserId = user, JobId = job, Status = "Redirected", ApplicationType = "External" });
                }
                catch (DuplicateApplicationException)
                {
                    return -1;
                }
            }))).GetAwaiter().GetResult();

            Assert.Single(outcomes, id => id > 0);
            Assert.Equal(9, outcomes.Count(id => id == -1));
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Applications WHERE user_id = @user AND job_id = @job AND is_deleted = 0", new { user, job }));
        }

        [DbFact]
        public void The_apply_limit_is_atomic_under_parallel_requests()
        {
            var employer = NewUser("employer");
            var user = NewUser();
            var jobs = Enumerable.Range(0, 12).Select(_ => NewInternalJob(employer)).ToList();
            var windowStart = DateTime.UtcNow.AddHours(-24);

            var results = Task.WhenAll(jobs.Select(job => Task.Run(() => _store.TryAddInternalApplication(
                new ApplicationModel { UserId = user, JobId = job, Status = "Submitted", ApplicationType = "Internal", AppliedAt = DateTime.UtcNow },
                limit: 5, windowStart)))).GetAwaiter().GetResult();

            Assert.Equal(5, results.Count(id => id.HasValue));
            Assert.Equal(7, results.Count(id => id is null));
            Assert.Equal(5, Count("SELECT COUNT(*) FROM Applications WHERE user_id = @user AND application_type = 'Internal'", new { user }));
        }

        // Each request stamps applied_at BEFORE it reaches the database, so parallel requests
        // insert in a different order than they took their locks. With range locks that made
        // some of them deadlock (SQL Server picked a victim -> a 500) even though the limit held.
        [DbFact]
        public void Parallel_applies_with_out_of_order_timestamps_neither_deadlock_nor_pass_the_limit()
        {
            var employer = NewUser("employer");
            var user = NewUser();
            var jobs = Enumerable.Range(0, 120).Select(_ => NewInternalJob(employer)).ToList();
            ThreadPool.SetMinThreads(200, 200);   // really run them all at once
            var windowStart = DateTime.UtcNow.AddHours(-24);
            var random = new Random(1234);
            var stamps = jobs.Select((_, i) => DateTime.UtcNow.AddMinutes(-random.Next(1, 1200)).AddTicks(i)).ToList();
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

            var results = Task.WhenAll(jobs.Select((job, i) => Task.Run(() =>
            {
                try
                {
                    return _store.TryAddInternalApplication(
                        new ApplicationModel { UserId = user, JobId = job, Status = "Submitted", ApplicationType = "Internal", AppliedAt = stamps[i] },
                        limit: 70, windowStart);
                }
                catch (Exception ex)
                {
                    failures.Add(ex.Message.Split('\n')[0]);
                    return (int?)-1;
                }
            }))).GetAwaiter().GetResult();

            Assert.True(failures.IsEmpty, "requests failed: " + string.Join(" | ", failures.Distinct()));
            Assert.Equal(70, results.Count(id => id > 0));
            Assert.Equal(50, results.Count(id => id is null));
            Assert.Equal(70, Count("SELECT COUNT(*) FROM Applications WHERE user_id = @user AND application_type = 'Internal'", new { user }));
        }

        [DbFact]
        public void The_limit_counts_withdrawn_applications_and_ignores_ones_outside_the_window()
        {
            var employer = NewUser("employer");
            var user = NewUser();
            var jobs = Enumerable.Range(0, 4).Select(_ => NewInternalJob(employer)).ToList();

            Application(0, DateTime.UtcNow.AddHours(-30));     // too old to count
            var withdrawn = Application(1, DateTime.UtcNow.AddHours(-1));
            _store.SoftDeleteApplication(withdrawn);            // withdrawn, but still counts
            Application(2, DateTime.UtcNow.AddMinutes(-5));

            var windowStart = DateTime.UtcNow.AddHours(-24);

            Assert.Null(TryAdd(3, limit: 2));                    // 2 already in the window (one withdrawn)
            Assert.NotNull(TryAdd(3, limit: 3));

            Assert.True(_store.GetOldestInternalApplicationSince(user, windowStart) is { } oldest &&
                        oldest > DateTime.UtcNow.AddHours(-2));

            int Application(int jobIndex, DateTime appliedAt) =>
                _store.TryAddInternalApplication(New(jobIndex, appliedAt), limit: 99, DateTime.UtcNow.AddDays(-2))!.Value;

            int? TryAdd(int jobIndex, int limit) =>
                _store.TryAddInternalApplication(New(jobIndex, DateTime.UtcNow), limit, windowStart);

            ApplicationModel New(int jobIndex, DateTime appliedAt) =>
                new() { UserId = user, JobId = jobs[jobIndex], Status = "Submitted", ApplicationType = "Internal", AppliedAt = appliedAt };
        }

        [DbFact]
        public void Update_changes_only_the_workflow_columns()
        {
            var user = NewUser();
            var job = _store.UpsertExternalListing(Imported());
            var id = _store.AddApplication(new ApplicationModel
            {
                UserId = user, JobId = job, Status = "Redirected", ApplicationType = "External", RedirectedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            var loaded = _store.GetApplication(id)!;
            loaded.Status = "Applied Externally";
            loaded.ConfirmedAt = loaded.AppliedAt = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);
            loaded.UserId = 99999;             // must be ignored
            loaded.ApplicationType = "Internal"; // must be ignored
            _store.UpdateApplication(loaded);

            var stored = _store.GetApplication(id)!;
            Assert.Equal("Applied Externally", stored.Status);
            Assert.Equal(new DateTime(2026, 9, 2), stored.ConfirmedAt);
            Assert.Equal(new DateTime(2026, 9, 1), stored.RedirectedAt);
            Assert.Equal(user, stored.UserId);
            Assert.Equal("External", stored.ApplicationType);
        }

        [DbFact]
        public void The_database_itself_refuses_a_status_that_does_not_match_the_type()
        {
            var employer = NewUser("employer");
            var user = NewUser();
            var job = NewInternalJob(employer);

            var wrong = Assert.Throws<SqlException>(() => _store.AddApplication(
                new ApplicationModel { UserId = user, JobId = job, Status = "Redirected", ApplicationType = "Internal" }));

            Assert.Contains("CK_Applications_type_status", wrong.Message);

            Assert.Throws<SqlException>(() => _store.AddApplication(
                new ApplicationModel { UserId = user, JobId = job, Status = "Submitted", ApplicationType = "External" }));

            Assert.Throws<SqlException>(() => _store.AddApplication(
                new ApplicationModel { UserId = user, JobId = job, Status = "Submitted", ApplicationType = "Neither" }));
        }

        // ----- Priority Application ---------------------------------------------

        [DbFact]
        public void Priority_is_stored_on_the_application_and_read_back()
        {
            var employer = NewUser("employer");
            var premiumUser = NewUser();
            var freeUser = NewUser();
            var job = NewInternalJob(employer);

            var priorityId = _store.TryAddInternalApplication(
                new ApplicationModel { UserId = premiumUser, JobId = job, Status = "Submitted", ApplicationType = "Internal", AppliedAt = DateTime.UtcNow, IsPriority = true },
                limit: 20, DateTime.UtcNow.AddDays(-1))!.Value;

            var normalId = _store.TryAddInternalApplication(
                new ApplicationModel { UserId = freeUser, JobId = job, Status = "Submitted", ApplicationType = "Internal", AppliedAt = DateTime.UtcNow },
                limit: 20, DateTime.UtcNow.AddDays(-1))!.Value;

            Assert.True(_store.GetApplication(priorityId)!.IsPriority);
            Assert.True(_store.FindActiveApplication(premiumUser, job)!.IsPriority);
            Assert.True(_store.GetApplicationsByUser(premiumUser).Single().IsPriority);
            Assert.False(_store.GetApplication(normalId)!.IsPriority);
        }

        [DbFact]
        public void An_application_is_not_priority_unless_it_says_so()
        {
            var employer = NewUser("employer");
            var user = NewUser();
            var job = NewInternalJob(employer);

            var id = _store.AddApplication(new ApplicationModel { UserId = user, JobId = job, Status = "Submitted", ApplicationType = "Internal", AppliedAt = DateTime.UtcNow });

            Assert.False(_store.GetApplication(id)!.IsPriority);
            Assert.Equal(0, Count("SELECT CAST(is_priority AS int) FROM Applications WHERE application_id = @id", new { id }));
        }

        [DbFact]
        public void The_database_refuses_a_priority_external_application()
        {
            var user = NewUser();
            var job = _store.UpsertExternalListing(Imported());

            using var db = Open();

            var refused = Assert.Throws<SqlException>(() => db.Execute(
                @"INSERT INTO Applications (user_id, job_id, status, is_deleted, application_type, redirected_at, is_priority)
                  VALUES (@user, @job, 'Redirected', 0, 'External', GETDATE(), 1)",
                new { user, job }));

            Assert.Contains("CK_Applications_priority_internal", refused.Message);
            Assert.Equal(0, Count("SELECT COUNT(*) FROM Applications WHERE user_id = @user", new { user }));
        }

        [DbFact]
        public void An_external_redirect_saved_through_the_store_is_never_priority()
        {
            var user = NewUser();
            var job = _store.UpsertExternalListing(Imported());

            var id = _store.AddApplication(new ApplicationModel { UserId = user, JobId = job, Status = "Redirected", ApplicationType = "External", IsPriority = true });

            Assert.False(_store.GetApplication(id)!.IsPriority);   // the external insert doesn't carry the flag at all
        }

        [DbFact]
        public void Updating_an_application_never_changes_its_priority()
        {
            var employer = NewUser("employer");
            var user = NewUser();
            var job = NewInternalJob(employer);
            var id = _store.TryAddInternalApplication(
                new ApplicationModel { UserId = user, JobId = job, Status = "Submitted", ApplicationType = "Internal", AppliedAt = DateTime.UtcNow, IsPriority = true },
                limit: 20, DateTime.UtcNow.AddDays(-1))!.Value;

            var loaded = _store.GetApplication(id)!;
            loaded.IsPriority = false;                    // must be ignored - priority is decided once, when applying
            loaded.Status = "Viewed";
            _store.UpdateApplication(loaded);

            Assert.True(_store.GetApplication(id)!.IsPriority);
            Assert.Equal("Viewed", _store.GetApplication(id)!.Status);
        }

        [DbFact]
        public void The_database_refuses_an_internal_job_without_an_employer_and_an_external_one_with_one()
        {
            var employer = NewUser("employer");

            using var db = Open();

            var noEmployer = Assert.Throws<SqlException>(() => db.Execute(
                "INSERT INTO Job_Listings (external_job_id, title, is_deleted, source, employer_id) VALUES (@e, @t, 0, 'Internal', NULL)",
                new { e = "x" + _tag, t = "bad " + _tag }));
            Assert.Contains("CK_Job_Listings_source", noEmployer.Message);

            Assert.Throws<SqlException>(() => db.Execute(
                "INSERT INTO Job_Listings (external_job_id, title, is_deleted, source, employer_id) VALUES (@e, @t, 0, 'External', @emp)",
                new { e = "y" + _tag, t = "bad " + _tag, emp = employer }));

            Assert.Throws<SqlException>(() => db.Execute(
                "INSERT INTO Job_Listings (external_job_id, title, is_deleted, source, apply_options) VALUES (@e, @t, 0, 'External', 'not json')",
                new { e = "z" + _tag, t = "bad " + _tag }));
        }

        // ----- people -----------------------------------------------------------

        // Notification creation itself moved to INotificationSender/SqlNotificationSender -
        // see SqlNotificationSenderDbTests - this is just the name/resume lookups AddNotification's
        // caller (ApplyService.NotifyEmployer) also depends on.
        [DbFact]
        public void Lookups_work_including_unicode_names()
        {
            var seeker = NewUser();

            Assert.Equal($"dbtest {_tag}", _store.GetUserName(seeker));
            Assert.Null(_store.GetUserName(int.MaxValue));
            Assert.Null(_store.GetPrimaryResumeId(seeker));
        }
    }
}
