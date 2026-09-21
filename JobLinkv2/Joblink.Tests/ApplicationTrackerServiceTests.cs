using JobLinkv2.Models;
using JobLinkv2.Services.Apply;
using Joblink.Tests.Support;
using Xunit;

namespace Joblink.Tests
{
    // The tracker's manual log used to accept any application row from anyone.
    // These check the rules that now stop it being used to skip the apply flow.
    public class ApplicationTrackerServiceTests
    {
        private const int Owner = 10;
        private const int Stranger = 11;
        private const int Employer = 99;

        private readonly InMemoryApplyStore _store = new();
        private readonly TestClock _clock = new();
        private readonly ApplicationTrackerService _tracker;
        private readonly ApplyService _apply;

        public ApplicationTrackerServiceTests()
        {
            _store.PrimaryResumes[Owner] = 5;
            _tracker = new ApplicationTrackerService(_store, _clock);
            _apply = new ApplyService(_store, _clock, new FakePlanReader());
        }

        private JoblistingModel ManualJob() =>
            _tracker.LogManualListing(new JoblistingModel { Title = "QA Tester", Company = "Acme" }).Value!;

        private ApplicationModel ManualApplication(string status = "Applied")
        {
            var job = ManualJob();
            var result = _tracker.LogManual(Owner, new ApplicationModel { JobId = job.JobId, Status = status });
            return _store.GetApplication(result.Value!.ApplicationId)!;
        }

        // ----- logging a job by hand --------------------------------------------

        [Fact]
        public void A_hand_logged_job_is_created_by_the_server_with_no_apply_data()
        {
            // A client trying to plant an apply link / employer / source on the listing.
            var result = _tracker.LogManualListing(new JoblistingModel
            {
                Title = "  QA Tester ",
                Company = "Acme",
                Location = "Manila",
                Source = "Internal",
                EmployerId = 7,
                SourceApi = "jsearch",
                ExternalJobId = "chosen-by-client",
                ApplyUrl = "https://evil.example/phish",
                ApplyIsDirect = true,
                Publisher = "LinkedIn",
                ApplyOptions = "[{\"apply_link\":\"https://evil.example\",\"is_direct\":true}]",
                IsExpired = true
            });

            var listing = result.Value!;

            Assert.True(result.IsOk);
            Assert.Equal("QA Tester", listing.Title);
            Assert.Equal("manual", listing.SourceApi);
            Assert.Equal("External", listing.Source);
            Assert.Null(listing.EmployerId);
            Assert.Null(listing.ApplyUrl);
            Assert.False(listing.ApplyIsDirect);
            Assert.Null(listing.Publisher);
            Assert.Null(listing.ApplyOptions);
            Assert.False(listing.IsExpired);
            Assert.StartsWith("manual-", listing.ExternalJobId);
            Assert.NotEqual("chosen-by-client", listing.ExternalJobId);
        }

        [Theory]
        [InlineData(null, "Acme")]
        [InlineData("QA", null)]
        [InlineData("   ", "Acme")]
        [InlineData("QA", "  ")]
        public void A_hand_logged_job_needs_a_title_and_company(string? title, string? company)
        {
            var result = _tracker.LogManualListing(new JoblistingModel { Title = title, Company = company });

            Assert.Equal(TrackerOutcome.Invalid, result.Outcome);
            Assert.Empty(_store.Listings);
        }

        [Fact]
        public void Overlong_text_is_rejected_not_silently_cut()
        {
            var result = _tracker.LogManualListing(new JoblistingModel { Title = new string('t', 301), Company = "Acme" });

            Assert.Equal(TrackerOutcome.Invalid, result.Outcome);
        }

        // ----- logging an application by hand -----------------------------------

        [Fact]
        public void A_hand_logged_application_is_External_for_the_caller_with_their_resume()
        {
            var job = ManualJob();

            var result = _tracker.LogManual(Owner, new ApplicationModel
            {
                UserId = Stranger,                    // ignored - the caller is who the token says
                JobId = job.JobId,
                Status = "Interview",
                ResumeId = 999,                       // ignored - the caller's own resume is used
                ApplicationType = "Internal"          // ignored
            });

            Assert.True(result.IsOk);

            var saved = Assert.Single(_store.Applications);
            Assert.Equal(Owner, saved.UserId);
            Assert.Equal(5, saved.ResumeId);
            Assert.Equal("External", saved.ApplicationType);
            Assert.Equal("Interview", saved.Status);
            Assert.Null(saved.RedirectedAt);
            Assert.Null(saved.ConfirmedAt);
        }

        [Fact]
        public void The_date_the_user_typed_is_kept()
        {
            var job = ManualJob();
            var typed = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);

            _tracker.LogManual(Owner, new ApplicationModel { JobId = job.JobId, Status = "Applied", AppliedAt = typed });

            Assert.Equal(typed, _store.Applications.Single().AppliedAt);
        }

        [Theory]
        [InlineData("Submitted")]
        [InlineData("Viewed")]
        [InlineData("Shortlisted")]
        [InlineData("Redirected")]
        [InlineData("Applied Externally")]
        [InlineData("Approved")]
        [InlineData("")]
        [InlineData(null)]
        public void Only_the_trackers_own_statuses_can_be_logged(string? status)
        {
            var job = ManualJob();

            var result = _tracker.LogManual(Owner, new ApplicationModel { JobId = job.JobId, Status = status });

            Assert.Equal(TrackerOutcome.Invalid, result.Outcome);
            Assert.Empty(_store.Applications);
        }

        [Theory]
        [InlineData("Applied")]
        [InlineData("Under Review")]
        [InlineData("Interview")]
        [InlineData("Offer")]
        [InlineData("Rejected")]
        public void Each_manual_status_is_accepted(string status)
        {
            var job = ManualJob();

            Assert.True(_tracker.LogManual(Owner, new ApplicationModel { JobId = job.JobId, Status = status }).IsOk);
        }

        [Fact]
        public void Applications_can_only_be_logged_against_jobs_added_by_hand()
        {
            var imported = _store.AddExternalJob();
            var employerJob = _store.AddInternalJob(Employer);

            Assert.Equal(TrackerOutcome.Invalid, _tracker.LogManual(Owner, new ApplicationModel { JobId = imported.JobId, Status = "Applied" }).Outcome);
            Assert.Equal(TrackerOutcome.Invalid, _tracker.LogManual(Owner, new ApplicationModel { JobId = employerJob.JobId, Status = "Applied" }).Outcome);
            Assert.Equal(TrackerOutcome.Invalid, _tracker.LogManual(Owner, new ApplicationModel { JobId = 424242, Status = "Applied" }).Outcome);
            Assert.Empty(_store.Applications);
        }

        [Fact]
        public void Logging_the_same_job_twice_is_a_conflict()
        {
            var job = ManualJob();

            Assert.True(_tracker.LogManual(Owner, new ApplicationModel { JobId = job.JobId, Status = "Applied" }).IsOk);
            Assert.Equal(TrackerOutcome.Conflict, _tracker.LogManual(Owner, new ApplicationModel { JobId = job.JobId, Status = "Offer" }).Outcome);
            Assert.Single(_store.Applications);
        }

        // ----- changing a status ------------------------------------------------

        [Fact]
        public void A_manual_application_can_move_between_manual_statuses()
        {
            var application = ManualApplication("Applied");

            var result = _tracker.ChangeStatus(Owner, application.ApplicationId, "Interview");

            Assert.True(result.IsOk);
            Assert.Equal("Interview", _store.GetApplication(application.ApplicationId)!.Status);
        }

        [Fact]
        public void Only_the_owner_can_change_a_status()
        {
            var application = ManualApplication("Applied");

            var result = _tracker.ChangeStatus(Stranger, application.ApplicationId, "Offer");

            Assert.Equal(TrackerOutcome.Forbidden, result.Outcome);
            Assert.Equal("Applied", _store.GetApplication(application.ApplicationId)!.Status);
        }

        [Theory]
        [InlineData("Submitted")]
        [InlineData("Viewed")]
        [InlineData("Shortlisted")]
        [InlineData("Redirected")]
        [InlineData("Applied Externally")]
        [InlineData("Nonsense")]
        [InlineData(null)]
        public void A_status_outside_the_trackers_own_set_is_rejected(string? newStatus)
        {
            var application = ManualApplication("Applied");

            var result = _tracker.ChangeStatus(Owner, application.ApplicationId, newStatus);

            Assert.Equal(TrackerOutcome.Invalid, result.Outcome);
            Assert.Equal("Applied", _store.GetApplication(application.ApplicationId)!.Status);
        }

        [Fact]
        public void The_status_of_an_internal_application_belongs_to_the_employer_not_the_user()
        {
            var job = _store.AddInternalJob(Employer);
            var applied = Assert.IsType<InternalApplied>(_apply.Apply(Owner, job.JobId));

            var result = _tracker.ChangeStatus(Owner, applied.ApplicationId, "Offer");

            Assert.Equal(TrackerOutcome.Managed, result.Outcome);
            Assert.Equal("Submitted", _store.GetApplication(applied.ApplicationId)!.Status);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_status_of_a_redirect_application_belongs_to_the_confirm_flow(bool confirmed)
        {
            var job = _store.AddExternalJob();
            var redirect = Assert.IsType<ExternalRedirect>(_apply.Apply(Owner, job.JobId));

            if (confirmed)
                _apply.ConfirmExternal(Owner, redirect.ApplicationId, true);

            var result = _tracker.ChangeStatus(Owner, redirect.ApplicationId, "Offer");

            Assert.Equal(TrackerOutcome.Managed, result.Outcome);
            Assert.Equal(confirmed ? "Applied Externally" : "Redirected", _store.GetApplication(redirect.ApplicationId)!.Status);
        }

        [Fact]
        public void Changing_a_status_leaves_everything_else_alone()
        {
            var application = ManualApplication("Applied");
            var before = (application.UserId, application.JobId, application.ApplicationType, application.AppliedAt, application.ResumeId);

            _tracker.ChangeStatus(Owner, application.ApplicationId, "Under Review");

            var after = _store.GetApplication(application.ApplicationId)!;
            Assert.Equal(before, (after.UserId, after.JobId, after.ApplicationType, after.AppliedAt, after.ResumeId));
        }

        [Fact]
        public void Changing_the_status_of_a_missing_application_is_not_found()
        {
            Assert.Equal(TrackerOutcome.NotFound, _tracker.ChangeStatus(Owner, 424242, "Offer").Outcome);
        }

        // ----- reading / withdrawing --------------------------------------------

        [Fact]
        public void Users_only_ever_see_their_own_applications()
        {
            ManualApplication();
            _store.AddApplicationRow(new ApplicationModel { UserId = Stranger, JobId = 77, Status = "Applied", ApplicationType = "External" });

            var mine = _tracker.List(Owner);

            Assert.Single(mine);
            Assert.All(mine, a => Assert.Equal(Owner, a.UserId));
        }

        [Fact]
        public void Reading_someone_elses_application_is_forbidden()
        {
            var application = ManualApplication();

            Assert.Equal(TrackerOutcome.Forbidden, _tracker.Get(Stranger, application.ApplicationId).Outcome);
            Assert.Equal(TrackerOutcome.NotFound, _tracker.Get(Owner, 424242).Outcome);
            Assert.True(_tracker.Get(Owner, application.ApplicationId).IsOk);
        }

        [Fact]
        public void Only_the_owner_can_withdraw_an_application()
        {
            var application = ManualApplication();

            Assert.Equal(TrackerOutcome.Forbidden, _tracker.Withdraw(Stranger, application.ApplicationId).Outcome);
            Assert.NotNull(_store.GetApplication(application.ApplicationId));

            Assert.True(_tracker.Withdraw(Owner, application.ApplicationId).IsOk);
            Assert.Null(_store.GetApplication(application.ApplicationId));
            Assert.Equal(TrackerOutcome.NotFound, _tracker.Withdraw(Owner, application.ApplicationId).Outcome);
        }

        [Fact]
        public void Withdrawing_internal_applications_does_not_free_up_the_apply_limit()
        {
            var jobs = Enumerable.Range(1, 21).Select(i => _store.AddInternalJob(Employer, $"Job {i}")).ToList();

            foreach (var job in jobs.Take(20))
            {
                var applied = Assert.IsType<InternalApplied>(_apply.Apply(Owner, job.JobId));
                Assert.True(_tracker.Withdraw(Owner, applied.ApplicationId).IsOk);
            }

            Assert.IsType<ApplyRateLimited>(_apply.Apply(Owner, jobs[20].JobId));
        }
    }
}
