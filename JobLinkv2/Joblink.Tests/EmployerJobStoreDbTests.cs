using Dapper;
using JobLinkv2.Models;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Employer;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // The real EmployerJobStore against the real Joblinkv2 database - credit spend, ownership and
    // the expiry filter are all things the SQL itself has to get right, not something an
    // in-memory fake could stand in for. Every test creates its own employers/jobs and removes
    // them afterwards. Needs JOBLINK_TEST_DB=1 (see tests/README.md).
    public sealed class EmployerJobStoreDbTests : IDisposable
    {
        private readonly EmployerJobStore _store = new(DbConfig.DefaultConnectionString);
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();
        private readonly DateTime _now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private int NewEmployer()
        {
            using var db = Open();

            var id = db.QuerySingle<int>(
                @"INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted)
                  VALUES (@name, @email, 'x', 'employer', GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest {_tag}", email = $"dbtest.{_tag}.{_userIds.Count}@example.com" });

            _userIds.Add(id);
            return id;
        }

        private JoblistingModel NewDraft(int employerId, string title = "Backend Developer") => _store.CreateDraft(new JoblistingModel
        {
            EmployerId = employerId,
            Title = $"{title} {_tag}",
            Company = "Acme",
            Description = "Build things.",
            Location = "Makati",
        });

        private void GivePostCredits(int employerId, int count = 1)
        {
            var option = JobPostCatalogue.Find(count >= 5 ? JobPostPackages.Bundle5 : JobPostPackages.Single)!;

            for (var granted = 0; granted < count;)
            {
                _store.RecordPurchase(employerId, option, _now);
                granted += option.Credits;
            }
        }

        private void GiveRenewalCredit(int employerId) =>
            _store.RecordPurchase(employerId, JobPostCatalogue.Find(JobPostPackages.Renewal)!, _now);

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? string.Join(",", _userIds) : "-1";

            db.Execute($@"
                DELETE FROM Applications WHERE job_id IN (SELECT job_id FROM Job_Listings WHERE employer_id IN ({users}));
                DELETE FROM Job_Listing_Skills WHERE job_id IN (SELECT job_id FROM Job_Listings WHERE employer_id IN ({users}));
                DELETE FROM Job_Listings WHERE employer_id IN ({users}) OR title LIKE '%{_tag}%';
                DELETE FROM Job_Post_Purchases WHERE employer_id IN ({users});
                DELETE FROM Users WHERE user_id IN ({users});");
        }

        // ----- publishing and credits --------------------------------------------

        [DbFact]
        public void Publishing_without_a_credit_fails_and_leaves_the_job_a_draft()
        {
            var employer = NewEmployer();
            var job = NewDraft(employer);

            var outcome = _store.Publish(employer, job.JobId, _now);

            Assert.Equal(PublishOutcome.NoCredit, outcome);
            Assert.Equal(JobStatuses.Draft, _store.GetOwnJob(employer, job.JobId)!.Status);
        }

        [DbFact]
        public void Publishing_spends_exactly_one_post_credit_and_makes_the_job_active_for_30_days()
        {
            var employer = NewEmployer();
            GivePostCredits(employer, 5);
            var job = NewDraft(employer);

            var outcome = _store.Publish(employer, job.JobId, _now);

            Assert.Equal(PublishOutcome.Ok, outcome);
            Assert.Equal(4, _store.CreditBalance(employer, JobCreditKinds.Post));

            var published = _store.GetOwnJob(employer, job.JobId)!;
            Assert.Equal(JobStatuses.Active, published.Status);
            Assert.Equal(_now, published.PublishedAt);
            Assert.Equal(_now.AddDays(JobPostCatalogue.ActiveDays), published.ExpiresAt);
        }

        [DbFact]
        public void A_second_publish_spends_a_second_credit_and_a_third_has_none_left()
        {
            var employer = NewEmployer();
            GivePostCredits(employer, 1);
            var first = NewDraft(employer, "First");
            var second = NewDraft(employer, "Second");

            Assert.Equal(PublishOutcome.Ok, _store.Publish(employer, first.JobId, _now));
            Assert.Equal(0, _store.CreditBalance(employer, JobCreditKinds.Post));
            Assert.Equal(PublishOutcome.NoCredit, _store.Publish(employer, second.JobId, _now));
        }

        [DbFact]
        public void Renewing_spends_a_renewal_credit_and_extends_from_the_current_expiry()
        {
            var employer = NewEmployer();
            GivePostCredits(employer, 1);
            GiveRenewalCredit(employer);
            var job = NewDraft(employer);
            _store.Publish(employer, job.JobId, _now);

            var outcome = _store.Renew(employer, job.JobId, _now);

            Assert.Equal(RenewOutcome.Ok, outcome);
            Assert.Equal(0, _store.CreditBalance(employer, JobCreditKinds.Renewal));
            Assert.Equal(_now.AddDays(JobPostCatalogue.ActiveDays * 2), _store.GetOwnJob(employer, job.JobId)!.ExpiresAt);
        }

        [DbFact]
        public void Renewing_without_a_credit_fails()
        {
            var employer = NewEmployer();
            GivePostCredits(employer, 1);
            var job = NewDraft(employer);
            _store.Publish(employer, job.JobId, _now);

            Assert.Equal(RenewOutcome.NoCredit, _store.Renew(employer, job.JobId, _now));
        }

        // ----- ownership -----------------------------------------------------------

        [DbFact]
        public void An_employer_cannot_edit_another_employers_job()
        {
            var owner = NewEmployer();
            var intruder = NewEmployer();
            var job = NewDraft(owner);

            var updated = _store.UpdateOwnJob(intruder, job.JobId, "Hacked", "Evil Co", "desc", "Nowhere", null, null, null, null);

            Assert.False(updated);
            Assert.Equal(job.Title, _store.GetOwnJob(owner, job.JobId)!.Title);
            Assert.Null(_store.GetOwnJob(intruder, job.JobId));
        }

        [DbFact]
        public void An_employer_cannot_publish_close_or_renew_another_employers_job()
        {
            var owner = NewEmployer();
            var intruder = NewEmployer();
            GivePostCredits(intruder, 1);
            GiveRenewalCredit(intruder);
            var job = NewDraft(owner);

            Assert.Equal(PublishOutcome.NotFound, _store.Publish(intruder, job.JobId, _now));
            Assert.False(_store.Close(intruder, job.JobId));
            Assert.Equal(RenewOutcome.NotFound, _store.Renew(intruder, job.JobId, _now));

            // The intruder's own credits were never touched.
            Assert.Equal(1, _store.CreditBalance(intruder, JobCreditKinds.Post));
            Assert.Equal(1, _store.CreditBalance(intruder, JobCreditKinds.Renewal));
        }

        // ----- visibility to job seekers --------------------------------------------

        [DbFact]
        public void Expired_and_closed_jobs_are_hidden_but_a_still_active_one_is_not()
        {
            var employer = NewEmployer();
            GivePostCredits(employer, 3);

            var active = NewDraft(employer, "Active");
            _store.Publish(employer, active.JobId, _now);

            var expired = NewDraft(employer, "Expired");
            _store.Publish(employer, expired.JobId, _now);

            var closed = NewDraft(employer, "Closed");
            _store.Publish(employer, closed.JobId, _now);
            _store.Close(employer, closed.JobId);

            // Force the second job's expiry into the past directly - ListActiveInternalJobs
            // filters by expires_at itself, it never depends on the lazy status flip.
            using (var db = Open())
                db.Execute("UPDATE Job_Listings SET expires_at = @past WHERE job_id = @id", new { past = _now.AddDays(-1), id = expired.JobId });

            var visible = _store.ListActiveInternalJobs(_now).Select(j => j.JobId).ToList();

            Assert.Contains(active.JobId, visible);
            Assert.DoesNotContain(expired.JobId, visible);
            Assert.DoesNotContain(closed.JobId, visible);
        }

        [DbFact]
        public void Listing_own_jobs_lazily_flips_a_stale_active_row_to_expired()
        {
            var employer = NewEmployer();
            GivePostCredits(employer, 1);
            var job = NewDraft(employer);
            _store.Publish(employer, job.JobId, _now);

            using (var db = Open())
                db.Execute("UPDATE Job_Listings SET expires_at = @past WHERE job_id = @id", new { past = _now.AddDays(-1), id = job.JobId });

            var summaries = _store.ListOwnJobs(employer, _now);

            Assert.Equal(JobStatuses.Expired, summaries.Single(s => s.Job.JobId == job.JobId).Job.Status);
        }

        [DbFact]
        public void Applicant_count_only_counts_internal_applications_for_that_job()
        {
            var employer = NewEmployer();
            GivePostCredits(employer, 1);
            var job = NewDraft(employer);
            _store.Publish(employer, job.JobId, _now);

            var applicant = NewEmployer(); // any user id works as a foreign key here

            using (var db = Open())
                db.Execute(
                    "INSERT INTO Applications (user_id, job_id, status, application_type, applied_at, is_deleted) VALUES (@userId, @jobId, 'Submitted', 'Internal', @now, 0)",
                    new { userId = applicant, jobId = job.JobId, now = _now });

            Assert.Equal(1, _store.GetApplicantCount(job.JobId));
        }
    }
}
