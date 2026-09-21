using JobLinkv2.Models;
using JobLinkv2.Services.Apply;
using Joblink.Tests.Support;
using Xunit;

namespace Joblink.Tests
{
    public class ApplyServiceTests
    {
        private const int Seeker = 10;
        private const int OtherSeeker = 11;
        private const int Employer = 99;

        private readonly InMemoryApplyStore _store = new();
        private readonly TestClock _clock = new();
        private readonly FakePlanReader _plans = new();
        private readonly ApplyService _service;

        public ApplyServiceTests()
        {
            _store.UserNames[Seeker] = "Maria Santos";
            _store.PrimaryResumes[Seeker] = 5;
            _service = new ApplyService(_store, _clock, _plans);
        }

        private List<JoblistingModel> InternalJobs(int count) =>
            Enumerable.Range(1, count).Select(i => _store.AddInternalJob(Employer, $"Job {i}")).ToList();

        private static string Options(params (string Link, bool IsDirect, string Publisher)[] entries) =>
            "[" + string.Join(",", entries.Select(e =>
                $"{{\"publisher\":\"{e.Publisher}\",\"apply_link\":\"{e.Link}\",\"is_direct\":{(e.IsDirect ? "true" : "false")}}}")) + "]";

        // =====================================================================
        // INTERNAL jobs - a normal application the employer receives
        // =====================================================================

        [Fact]
        public void Internal_apply_creates_a_Submitted_application()
        {
            var job = _store.AddInternalJob(Employer);

            var result = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.Equal("Submitted", result.Status);
            Assert.False(result.AlreadyApplied);

            var saved = Assert.Single(_store.Applications);
            Assert.Equal(result.ApplicationId, saved.ApplicationId);
            Assert.Equal(Seeker, saved.UserId);
            Assert.Equal(job.JobId, saved.JobId);
            Assert.Equal("Submitted", saved.Status);
            Assert.Equal("Internal", saved.ApplicationType);
            Assert.Equal(5, saved.ResumeId);                 // the seeker's resume goes with it
            Assert.Equal(_clock.UtcNow, saved.AppliedAt);
            Assert.Null(saved.RedirectedAt);
            Assert.Null(saved.ConfirmedAt);
        }

        [Fact]
        public void Internal_apply_notifies_the_employer()
        {
            var job = _store.AddInternalJob(Employer, "Backend Developer");

            _service.Apply(Seeker, job.JobId);

            var (userId, message) = Assert.Single(_store.Notifications);
            Assert.Equal(Employer, userId);
            Assert.Contains("Maria Santos", message);
            Assert.Contains("Backend Developer", message);
        }

        [Fact]
        public void A_failed_notification_does_not_fail_the_application()
        {
            var job = _store.AddInternalJob(Employer);
            _store.NotificationsFail = true;

            var result = _service.Apply(Seeker, job.JobId);

            Assert.IsType<InternalApplied>(result);
            Assert.Single(_store.Applications);
        }

        [Fact]
        public void Applying_without_a_resume_still_works()
        {
            var job = _store.AddInternalJob(Employer);

            var result = _service.Apply(OtherSeeker, job.JobId);

            Assert.IsType<InternalApplied>(result);
            Assert.Null(_store.Applications.Single().ResumeId);
        }

        [Fact]
        public void Applying_again_returns_the_existing_application_flagged_alreadyApplied()
        {
            var job = _store.AddInternalJob(Employer);

            var first = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));
            var second = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.False(first.AlreadyApplied);
            Assert.True(second.AlreadyApplied);
            Assert.Equal(first.ApplicationId, second.ApplicationId);
            Assert.Equal("Submitted", second.Status);
            Assert.Single(_store.Applications);
            Assert.Single(_store.Notifications);             // the employer is told once
        }

        [Fact]
        public void A_duplicate_that_loses_a_race_is_reported_as_alreadyApplied()
        {
            var job = _store.AddInternalJob(Employer);
            _store.LoseNextInsertRace = true;

            var result = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.True(result.AlreadyApplied);
            Assert.Single(_store.Applications);
        }

        [Fact]
        public void Applying_to_a_job_that_was_soft_deleted_is_not_found()
        {
            var job = _store.AddInternalJob(Employer);
            job.IsDeleted = true;

            Assert.IsType<ApplyJobNotFound>(_service.Apply(Seeker, job.JobId));
            Assert.IsType<ApplyJobNotFound>(_service.Apply(Seeker, 424242));
        }

        [Fact]
        public void A_withdrawn_application_can_be_replaced_by_a_new_one()
        {
            var job = _store.AddInternalJob(Employer);
            var first = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));
            _store.SoftDeleteApplication(first.ApplicationId);

            var again = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.False(again.AlreadyApplied);
            Assert.NotEqual(first.ApplicationId, again.ApplicationId);
        }

        // =====================================================================
        // PRIORITY APPLICATION - Premium at the moment of applying, internal jobs only
        // =====================================================================

        [Fact]
        public void A_premium_applicant_makes_a_priority_application()
        {
            _plans.MakePremium(Seeker);
            var job = _store.AddInternalJob(Employer);

            var result = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.True(result.IsPriority);
            Assert.True(Assert.Single(_store.Applications).IsPriority);
        }

        [Fact]
        public void A_free_applicant_makes_a_normal_application()
        {
            var job = _store.AddInternalJob(Employer);

            var result = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.False(result.IsPriority);
            Assert.False(Assert.Single(_store.Applications).IsPriority);
        }

        [Fact]
        public void An_external_redirect_is_never_priority_and_never_asks_for_the_plan()
        {
            _plans.MakePremium(Seeker);
            var job = _store.AddExternalJob();

            Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            Assert.False(Assert.Single(_store.Applications).IsPriority);
            Assert.Equal(0, _plans.Asked);
        }

        [Fact]
        public void Priority_is_a_snapshot_from_the_moment_of_applying()
        {
            var early = _store.AddInternalJob(Employer, "Early");
            var late = _store.AddInternalJob(Employer, "Late");

            // Premium when applying, then the plan lapses: the application stays priority...
            _plans.MakePremium(Seeker);
            var first = Assert.IsType<InternalApplied>(_service.Apply(Seeker, early.JobId));
            _plans.MakeFree(Seeker);

            Assert.True(first.IsPriority);
            Assert.True(_store.GetApplication(first.ApplicationId)!.IsPriority);

            // ...and a job applied to after the lapse is a normal application.
            Assert.False(Assert.IsType<InternalApplied>(_service.Apply(Seeker, late.JobId)).IsPriority);
        }

        [Fact]
        public void Upgrading_later_does_not_turn_an_earlier_application_into_priority()
        {
            var job = _store.AddInternalJob(Employer);
            var first = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            _plans.MakePremium(Seeker);
            var again = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));    // "apply again"

            Assert.False(first.IsPriority);
            Assert.True(again.AlreadyApplied);
            Assert.False(again.IsPriority);                       // it reports what was stored
            Assert.False(Assert.Single(_store.Applications).IsPriority);
        }

        [Fact]
        public void Applying_again_reports_the_stored_priority_after_the_plan_lapsed()
        {
            _plans.MakePremium(Seeker);
            var job = _store.AddInternalJob(Employer);
            Assert.True(Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId)).IsPriority);

            _plans.MakeFree(Seeker);
            var again = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.True(again.AlreadyApplied);
            Assert.True(again.IsPriority);
        }

        [Fact]
        public void A_priority_application_tells_the_employer_it_is_priority()
        {
            _plans.MakePremium(Seeker);
            var job = _store.AddInternalJob(Employer, "Backend Developer");

            _service.Apply(Seeker, job.JobId);

            var (userId, message) = Assert.Single(_store.Notifications);
            Assert.Equal(Employer, userId);
            Assert.Equal("Priority application: Maria Santos applied for Backend Developer.", message);
        }

        [Fact]
        public void A_normal_application_notification_does_not_say_priority()
        {
            var job = _store.AddInternalJob(Employer, "Backend Developer");

            _service.Apply(Seeker, job.JobId);

            var (_, message) = Assert.Single(_store.Notifications);
            Assert.Equal("Maria Santos applied for Backend Developer.", message);
            Assert.DoesNotContain("Priority", message);
        }

        [Fact]
        public void A_plan_that_cannot_be_read_applies_as_a_normal_application()
        {
            _plans.Broken = true;
            var job = _store.AddInternalJob(Employer);

            var result = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.False(result.IsPriority);                      // applying is never blocked by the plan lookup
            Assert.Single(_store.Applications);
            Assert.Single(_store.Notifications);
        }

        [Fact]
        public void The_plan_is_asked_once_per_new_internal_application_and_never_for_a_repeat()
        {
            _plans.MakePremium(Seeker);
            var job = _store.AddInternalJob(Employer);

            _service.Apply(Seeker, job.JobId);
            Assert.Equal(1, _plans.Asked);

            _service.Apply(Seeker, job.JobId);                    // duplicate: hands back the first
            Assert.Equal(1, _plans.Asked);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Premium_and_free_have_the_same_20_per_day_limit(bool premium)
        {
            if (premium)
                _plans.MakePremium(Seeker);

            var jobs = InternalJobs(21);

            foreach (var job in jobs.Take(20))
                Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            var limited = Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));

            Assert.Equal(24 * 3600, limited.RetryAfterSeconds);
            Assert.Equal(20, _store.Applications.Count);
            Assert.Equal(20, _store.Notifications.Count);
        }

        [Fact]
        public void A_premium_applicant_can_still_redirect_to_external_jobs_at_the_internal_limit()
        {
            _plans.MakePremium(Seeker);

            foreach (var job in InternalJobs(20))
                _service.Apply(Seeker, job.JobId);

            var external = _store.AddExternalJob();

            Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, external.JobId));   // redirects never count
        }

        // =====================================================================
        // RATE LIMIT - 20 internal applications per user per rolling 24 hours
        // =====================================================================

        [Fact]
        public void The_limit_is_20_per_24_hours()
        {
            Assert.Equal(20, ApplyService.InternalApplicationLimit);
            Assert.Equal(TimeSpan.FromHours(24), ApplyService.InternalApplicationWindow);
        }

        [Fact]
        public void The_21st_internal_application_in_a_day_is_rate_limited()
        {
            var jobs = InternalJobs(21);

            foreach (var job in jobs.Take(20))
                Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            var limited = Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));

            Assert.Equal(24 * 3600, limited.RetryAfterSeconds);      // all 20 happened "now"
            Assert.Equal(20, _store.Applications.Count);              // nothing was created
            Assert.Equal(20, _store.Notifications.Count);             // and the employer wasn't told
        }

        [Fact]
        public void Duplicate_applications_do_not_count_toward_the_limit()
        {
            var jobs = InternalJobs(21);

            foreach (var job in jobs.Take(19))
                _service.Apply(Seeker, job.JobId);

            // Ten more clicks on jobs already applied to use up nothing...
            for (var i = 0; i < 10; i++)
                Assert.True(Assert.IsType<InternalApplied>(_service.Apply(Seeker, jobs[i].JobId)).AlreadyApplied);

            // ...so the 20th real application still goes through, and only the 21st is blocked.
            Assert.False(Assert.IsType<InternalApplied>(_service.Apply(Seeker, jobs[19].JobId)).AlreadyApplied);
            Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));
        }

        [Fact]
        public void Repeating_an_application_is_still_allowed_at_the_limit()
        {
            var jobs = InternalJobs(21);

            foreach (var job in jobs.Take(20))
                _service.Apply(Seeker, job.JobId);

            var repeat = Assert.IsType<InternalApplied>(_service.Apply(Seeker, jobs[0].JobId));

            Assert.True(repeat.AlreadyApplied);              // not a 429
        }

        [Fact]
        public void The_window_rolls_and_older_applications_stop_counting()
        {
            var jobs = InternalJobs(22);

            _service.Apply(Seeker, jobs[0].JobId);                       // t = 0h

            _clock.Advance(TimeSpan.FromHours(10));
            foreach (var job in jobs.Skip(1).Take(19))                   // t = 10h -> 20 in the window
                _service.Apply(Seeker, job.JobId);

            _clock.Advance(TimeSpan.FromHours(13));                      // t = 23h
            var limited = Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));
            Assert.Equal(3600, limited.RetryAfterSeconds);               // the first one leaves the window in 1h

            _clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));   // t = 24h 0m 1s
            Assert.IsType<InternalApplied>(_service.Apply(Seeker, jobs[20].JobId));
        }

        [Fact]
        public void External_redirects_do_not_count_toward_the_limit()
        {
            var external = Enumerable.Range(1, 30).Select(_ => _store.AddExternalJob()).ToList();
            var jobs = InternalJobs(21);

            foreach (var job in external)
                Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            // 30 redirects later, all 20 internal applications are still available.
            foreach (var job in jobs.Take(20))
                Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));
        }

        [Fact]
        public void Being_at_the_limit_does_not_block_external_redirects()
        {
            var jobs = InternalJobs(21);

            foreach (var job in jobs.Take(20))
                _service.Apply(Seeker, job.JobId);

            Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));
            Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, _store.AddExternalJob().JobId));
        }

        [Fact]
        public void Withdrawn_applications_still_count_so_apply_and_withdraw_cannot_dodge_the_limit()
        {
            var jobs = InternalJobs(21);

            foreach (var job in jobs.Take(20))
            {
                var applied = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));
                _store.SoftDeleteApplication(applied.ApplicationId);
            }

            Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));
        }

        [Fact]
        public void The_limit_is_per_user()
        {
            var jobs = InternalJobs(21);

            foreach (var job in jobs.Take(20))
                _service.Apply(Seeker, job.JobId);

            Assert.IsType<ApplyRateLimited>(_service.Apply(Seeker, jobs[20].JobId));
            Assert.IsType<InternalApplied>(_service.Apply(OtherSeeker, jobs[20].JobId));
        }

        [Fact]
        public void Logging_applications_by_hand_does_not_use_up_the_limit()
        {
            // Tracker-logged rows are External, so they're never counted.
            for (var i = 0; i < 30; i++)
                _store.AddApplicationRow(new ApplicationModel
                {
                    UserId = Seeker, JobId = 1000 + i, Status = "Applied",
                    ApplicationType = "External", AppliedAt = _clock.UtcNow
                });

            var jobs = InternalJobs(20);

            foreach (var job in jobs)
                Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));
        }

        // =====================================================================
        // EXTERNAL jobs - record the click, then send the user to the posting
        // =====================================================================

        [Fact]
        public void External_apply_records_a_Redirected_application_and_returns_the_link()
        {
            var job = _store.AddExternalJob("https://www.linkedin.com/jobs/view/123", publisher: "LinkedIn");

            var result = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            Assert.Equal("https://www.linkedin.com/jobs/view/123", result.RedirectUrl);
            Assert.Equal("LinkedIn", result.Publisher);
            Assert.Equal("Redirected", result.Status);

            var saved = Assert.Single(_store.Applications);
            Assert.Equal(result.ApplicationId, saved.ApplicationId);
            Assert.Equal("External", saved.ApplicationType);
            Assert.Equal("Redirected", saved.Status);
            Assert.Equal(_clock.UtcNow, saved.RedirectedAt);
            Assert.Null(saved.AppliedAt);                    // not applied until the user confirms
            Assert.Null(saved.ConfirmedAt);
            Assert.Empty(_store.Notifications);              // there is no employer to tell
        }

        [Fact]
        public void The_redirect_link_follows_the_priority_order()
        {
            var direct = "https://jobs.acme.example/apply/1";
            var options = Options(("https://www.indeed.com/1", false, "Indeed"), ("https://careers.acme.example/2", true, "Acme Careers"));

            // (a) apply_url, because it is direct
            var a = _store.AddExternalJob(direct, applyIsDirect: true, applyOptionsJson: options);
            // (b) first direct option
            var b = _store.AddExternalJob("https://www.linkedin.com/9", applyIsDirect: false, applyOptionsJson: options);
            // (c) apply_url, when no option is direct
            var c = _store.AddExternalJob("https://www.linkedin.com/9", applyIsDirect: false,
                applyOptionsJson: Options(("https://www.indeed.com/1", false, "Indeed")));
            // (d) first option, when there's no apply_url
            var d = _store.AddExternalJob(null, applyIsDirect: false,
                applyOptionsJson: Options(("https://www.indeed.com/1", false, "Indeed"), ("https://www.glassdoor.com/2", false, "Glassdoor")));

            Assert.Equal(direct, UrlFor(a));
            Assert.Equal("https://careers.acme.example/2", UrlFor(b));
            Assert.Equal("https://www.linkedin.com/9", UrlFor(c));
            Assert.Equal("https://www.indeed.com/1", UrlFor(d));

            string UrlFor(JoblistingModel job) => Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId)).RedirectUrl;
        }

        [Fact]
        public void The_publisher_comes_from_the_option_when_the_link_came_from_an_option()
        {
            var options = Options(("https://careers.acme.example/2", true, "Acme Careers"));
            var job = _store.AddExternalJob("https://www.linkedin.com/9", applyOptionsJson: options, publisher: "LinkedIn");

            var result = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            Assert.Equal("Acme Careers", result.Publisher);
        }

        [Fact]
        public void Expired_links_are_skipped_in_favour_of_a_working_one()
        {
            var options = Options(
                ("https://jobs.example/expired/direct", true, "Dead"),
                ("https://www.indeed.com/working", false, "Indeed"));
            var job = _store.AddExternalJob("https://www.linkedin.com/expired-posting", applyIsDirect: true, applyOptionsJson: options);

            var result = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            Assert.Equal("https://www.indeed.com/working", result.RedirectUrl);
            Assert.False(job.IsExpired);
        }

        [Fact]
        public void With_no_valid_link_left_the_job_is_marked_expired_and_no_application_is_created()
        {
            var options = Options(("https://jobs.example/expired/1", true, "Dead"), ("http://insecure.example/2", false, "Insecure"));
            var job = _store.AddExternalJob("https://www.linkedin.com/expired/9", applyIsDirect: true, applyOptionsJson: options);

            var result = Assert.IsType<ApplyExpired>(_service.Apply(Seeker, job.JobId));

            Assert.Equal("LinkedIn", result.Publisher);
            Assert.True(job.IsExpired);
            Assert.Empty(_store.Applications);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("http://not-secure.example/apply")]
        [InlineData("javascript:alert(1)")]
        public void A_job_with_only_unusable_links_is_treated_as_expired(string? applyUrl)
        {
            var job = _store.AddExternalJob(applyUrl);

            Assert.IsType<ApplyExpired>(_service.Apply(Seeker, job.JobId));
            Assert.True(job.IsExpired);
        }

        [Fact]
        public void A_job_logged_by_hand_has_no_link_so_it_cannot_be_applied_to()
        {
            var manual = _store.AddListing(new JoblistingModel { Title = "Chef", Company = "Cafe", SourceApi = "manual" });

            Assert.IsType<ApplyExpired>(_service.Apply(Seeker, manual.JobId));
        }

        [Fact]
        public void A_second_click_reuses_the_existing_application_instead_of_creating_a_duplicate()
        {
            var job = _store.AddExternalJob();

            var first = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));
            var redirectedAt = _store.Applications.Single().RedirectedAt;

            _clock.Advance(TimeSpan.FromMinutes(5));
            var second = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            Assert.Equal(first.ApplicationId, second.ApplicationId);
            Assert.Equal(first.RedirectUrl, second.RedirectUrl);          // the user still gets sent to the posting
            Assert.Equal("Redirected", second.Status);
            Assert.Single(_store.Applications);
            Assert.Equal(redirectedAt, _store.Applications.Single().RedirectedAt);   // and nothing was rewritten
        }

        [Fact]
        public void Clicking_again_after_confirming_keeps_the_confirmed_status()
        {
            var job = _store.AddExternalJob();
            var first = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));
            _service.ConfirmExternal(Seeker, first.ApplicationId, applied: true);

            var again = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            Assert.Equal(first.ApplicationId, again.ApplicationId);
            Assert.Equal("Applied Externally", again.Status);
            Assert.Single(_store.Applications);
        }

        [Fact]
        public void A_duplicate_that_loses_a_race_reuses_the_winning_application()
        {
            var job = _store.AddExternalJob();
            _store.LoseNextInsertRace = true;

            var result = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));

            Assert.Single(_store.Applications);
            Assert.Equal(_store.Applications.Single().ApplicationId, result.ApplicationId);
        }

        [Fact]
        public void Different_users_each_get_their_own_application_for_the_same_job()
        {
            var job = _store.AddExternalJob();

            var a = Assert.IsType<ExternalRedirect>(_service.Apply(Seeker, job.JobId));
            var b = Assert.IsType<ExternalRedirect>(_service.Apply(OtherSeeker, job.JobId));

            Assert.NotEqual(a.ApplicationId, b.ApplicationId);
            Assert.Equal(2, _store.Applications.Count);
        }

        [Fact]
        public void An_expired_job_stays_expired_even_for_someone_who_already_clicked_it()
        {
            var job = _store.AddExternalJob("https://www.linkedin.com/jobs/1");
            _service.Apply(Seeker, job.JobId);

            job.ApplyUrl = "https://www.linkedin.com/jobs/expired";      // the posting later goes dead

            Assert.IsType<ApplyExpired>(_service.Apply(Seeker, job.JobId));
        }

        // =====================================================================
        // CONFIRM - "Did you finish applying on {publisher}?"
        // =====================================================================

        private int Redirect(int userId = Seeker)
        {
            var job = _store.AddExternalJob();
            return Assert.IsType<ExternalRedirect>(_service.Apply(userId, job.JobId)).ApplicationId;
        }

        [Fact]
        public void Confirming_yes_marks_it_Applied_Externally()
        {
            var id = Redirect();
            _clock.Advance(TimeSpan.FromMinutes(20));

            var result = Assert.IsType<ConfirmChanged>(_service.ConfirmExternal(Seeker, id, applied: true));

            Assert.Equal("Applied Externally", result.Application.Status);
            Assert.Equal(_clock.UtcNow, result.Application.ConfirmedAt);

            var saved = _store.GetApplication(id)!;
            Assert.Equal("Applied Externally", saved.Status);
            Assert.Equal(_clock.UtcNow, saved.ConfirmedAt);
            Assert.Equal(_clock.UtcNow, saved.AppliedAt);     // so the tracker can sort by it
        }

        [Fact]
        public void Confirming_no_leaves_it_Redirected()
        {
            var id = Redirect();

            var result = Assert.IsType<ConfirmUnchanged>(_service.ConfirmExternal(Seeker, id, applied: false));

            Assert.Equal("Redirected", result.Application.Status);
            var saved = _store.GetApplication(id)!;
            Assert.Equal("Redirected", saved.Status);
            Assert.Null(saved.ConfirmedAt);
            Assert.Null(saved.AppliedAt);
        }

        [Fact]
        public void Someone_who_said_Not_yet_can_confirm_later()
        {
            var id = Redirect();

            _service.ConfirmExternal(Seeker, id, applied: false);
            var later = _service.ConfirmExternal(Seeker, id, applied: true);

            Assert.IsType<ConfirmChanged>(later);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Only_the_owner_can_confirm(bool applied)
        {
            var id = Redirect(Seeker);

            Assert.IsType<ConfirmForbidden>(_service.ConfirmExternal(OtherSeeker, id, applied));

            Assert.Equal("Redirected", _store.GetApplication(id)!.Status);   // untouched
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Confirming_an_already_confirmed_application_is_rejected(bool applied)
        {
            var id = Redirect();
            _service.ConfirmExternal(Seeker, id, applied: true);

            var result = Assert.IsType<ConfirmWrongStatus>(_service.ConfirmExternal(Seeker, id, applied));

            Assert.Equal("Applied Externally", result.CurrentStatus);
        }

        [Fact]
        public void Internal_applications_cannot_be_confirmed()
        {
            var job = _store.AddInternalJob(Employer);
            var applied = Assert.IsType<InternalApplied>(_service.Apply(Seeker, job.JobId));

            var result = Assert.IsType<ConfirmWrongStatus>(_service.ConfirmExternal(Seeker, applied.ApplicationId, true));

            Assert.Equal("Submitted", result.CurrentStatus);
        }

        [Theory]
        [InlineData("Applied")]
        [InlineData("Under Review")]
        [InlineData("Interview")]
        [InlineData("Offer")]
        [InlineData("Rejected")]
        public void Applications_logged_by_hand_cannot_be_confirmed(string status)
        {
            var row = _store.AddApplicationRow(new ApplicationModel
            {
                UserId = Seeker, JobId = 500, Status = status, ApplicationType = "External", AppliedAt = _clock.UtcNow
            });

            Assert.IsType<ConfirmWrongStatus>(_service.ConfirmExternal(Seeker, row.ApplicationId, true));
            Assert.Equal(status, _store.GetApplication(row.ApplicationId)!.Status);
        }

        [Fact]
        public void Confirming_an_unknown_or_withdrawn_application_is_not_found()
        {
            var id = Redirect();
            _store.SoftDeleteApplication(id);

            Assert.IsType<ConfirmNotFound>(_service.ConfirmExternal(Seeker, id, true));
            Assert.IsType<ConfirmNotFound>(_service.ConfirmExternal(Seeker, 424242, true));
        }
    }
}
