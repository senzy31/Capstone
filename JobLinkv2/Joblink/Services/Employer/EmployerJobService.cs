using JobLinkv2.Models;
using JobLinkv2.Services.Employer;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Notifications;

namespace Joblink.Services.Employer
{
    public enum PurchaseOutcome { Ok, InvalidPackage }

    // Orchestrates paid employer job posting: validates what a client sends (JobPostingRules),
    // spends/grants credits and reads/writes jobs (EmployerJobStore), resolves skill names
    // against the shared catalogue (SkillStore), geocodes a job's location on publish
    // (IGeocodingService, best-effort), and tells the employer what happened (INotificationSender).
    public sealed class EmployerJobService
    {
        private readonly EmployerJobStore _jobs;
        private readonly SkillStore _skills;
        private readonly IGeocodingService _geocoder;
        private readonly INotificationSender _notifications;
        private readonly TimeProvider _time;
        private readonly ILogger<EmployerJobService> _logger;

        public EmployerJobService(
            EmployerJobStore jobs, SkillStore skills, IGeocodingService geocoder,
            INotificationSender notifications, TimeProvider time, ILogger<EmployerJobService> logger)
        {
            _jobs = jobs;
            _skills = skills;
            _geocoder = geocoder;
            _notifications = notifications;
            _time = time;
            _logger = logger;
        }

        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        public IReadOnlyList<PostingPackageView> ListPackages() =>
            JobPostCatalogue.Options.Select(o => new PostingPackageView(o.Package, o.CreditKind, o.PricePhp, o.Credits, o.Description)).ToList();

        public (PurchaseOutcome Outcome, PurchaseView? View) Purchase(int employerId, string? package)
        {
            var option = JobPostCatalogue.Find(package);

            if (option is null)
                return (PurchaseOutcome.InvalidPackage, null);

            var purchase = _jobs.RecordPurchase(employerId, option, UtcNow);

            _notifications.Send(employerId, NotificationTypes.PurchaseConfirmed,
                $"Payment confirmed: {option.Description} (PHP {option.PricePhp:0.##}).", "/employer/jobs");

            var balance = new CreditsView(
                _jobs.CreditBalance(employerId, JobCreditKinds.Post),
                _jobs.CreditBalance(employerId, JobCreditKinds.Renewal));

            return (PurchaseOutcome.Ok, new PurchaseView(purchase.Package, purchase.AmountPhp, purchase.CreditsGranted, purchase.PurchasedAt, balance));
        }

        public (string? Error, EmployerJobView? View) CreateDraft(int employerId, JobRequest request)
        {
            var error = Validate(request);

            if (error != null)
                return (error, null);

            var job = _jobs.CreateDraft(new JoblistingModel
            {
                EmployerId = employerId,
                Title = request.Title!.Trim(),
                Company = request.Company!.Trim(),
                Description = request.Description!.Trim(),
                Location = request.Location!.Trim(),
                SalaryMin = request.SalaryMin,
                SalaryMax = request.SalaryMax,
                WorkSetup = request.WorkSetup,
                JobType = request.JobType
            });

            var skills = ApplySkills(job.JobId, request.Skills);

            return (null, EmployerJobView.From(new JoblistingModelWithSkills(job, 0, skills), UtcNow));
        }

        public (string? Error, bool NotFound, EmployerJobView? View) UpdateJob(int employerId, int jobId, JobRequest request)
        {
            var error = Validate(request);

            if (error != null)
                return (error, false, null);

            var updated = _jobs.UpdateOwnJob(
                employerId, jobId, request.Title!.Trim(), request.Company!.Trim(), request.Description!.Trim(),
                request.Location!.Trim(), request.SalaryMin, request.SalaryMax, request.WorkSetup, request.JobType);

            if (!updated)
                return (null, true, null);

            var skills = ApplySkills(jobId, request.Skills);

            var job = _jobs.GetOwnJob(employerId, jobId)!;
            var applicantCount = _jobs.GetApplicantCount(jobId);

            return (null, false, EmployerJobView.From(new JoblistingModelWithSkills(job, applicantCount, skills), UtcNow));
        }

        public async Task<(PublishOutcome Outcome, EmployerJobView? View)> PublishAsync(int employerId, int jobId, CancellationToken cancellationToken)
        {
            var outcome = _jobs.Publish(employerId, jobId, UtcNow);

            if (outcome != PublishOutcome.Ok)
                return (outcome, null);

            var job = _jobs.GetOwnJob(employerId, jobId)!;

            await GeocodeBestEffort(job, cancellationToken);

            _notifications.Send(employerId, NotificationTypes.JobPublished,
                $"\"{job.Title}\" is now live on JobLink for the next {JobPostCatalogue.ActiveDays} days.", "/employer/jobs");

            return (PublishOutcome.Ok, DescribeOwnJob(employerId, jobId));
        }

        public bool Close(int employerId, int jobId) => _jobs.Close(employerId, jobId);

        public RenewOutcome Renew(int employerId, int jobId) => _jobs.Renew(employerId, jobId, UtcNow);

        public EmployerJobView? DescribeOwnJob(int employerId, int jobId)
        {
            var job = _jobs.GetOwnJob(employerId, jobId);

            if (job is null)
                return null;

            var applicantCount = _jobs.GetApplicantCount(jobId);
            var skills = _jobs.GetSkills(jobId).Select(s => s.SkillName ?? "").ToList();

            return EmployerJobView.From(new JoblistingModelWithSkills(job, applicantCount, skills), UtcNow);
        }

        // This employer's jobs, newest first. Also where the "no background job" expiry
        // notifications fire from - checked every time the employer looks at their own list,
        // never on a schedule.
        public IReadOnlyList<EmployerJobView> ListOwnJobs(int employerId)
        {
            var now = UtcNow;
            var summaries = _jobs.ListOwnJobs(employerId, now);

            var views = new List<EmployerJobView>(summaries.Count);

            foreach (var summary in summaries)
            {
                var skills = _jobs.GetSkills(summary.Job.JobId).Select(s => s.SkillName ?? "").ToList();

                NotifyExpiry(employerId, summary.Job, now);

                views.Add(EmployerJobView.From(new JoblistingModelWithSkills(summary.Job, summary.ApplicantCount, skills), now));
            }

            return views;
        }

        // ----- internals ------------------------------------------------------------

        private static string? Validate(JobRequest request) => JobPostingRules.Validate(
            request.Title, request.Company, request.Description, request.Location,
            request.SalaryMin, request.SalaryMax, request.WorkSetup, request.JobType);

        private IReadOnlyList<string> ApplySkills(int jobId, List<string>? names)
        {
            var distinct = (names ?? new List<string>())
                .Select(n => n?.Trim())
                .Where(n => !string.IsNullOrEmpty(n) && n.Length <= 100)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(JobPostingRules.MaxSkills)
                .ToList();

            var resolved = distinct.Select(n => _skills.AddOrGet(n!)).ToList();

            _jobs.SetSkills(jobId, resolved.Select(s => s.SkillId).ToList());

            return resolved.Select(s => s.SkillName ?? "").ToList();
        }

        private async Task GeocodeBestEffort(JoblistingModel job, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(job.Location))
                return;

            try
            {
                var located = await _geocoder.GeocodeAsync(job.Location, cancellationToken);

                if (located != null)
                    _jobs.SetCoordinates(job.JobId, located.Latitude, located.Longitude);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Geocoding failed for job {JobId}; publishing without coordinates.", job.JobId);
            }
        }

        private void NotifyExpiry(int employerId, JoblistingModel job, DateTime now)
        {
            var effective = JobPostingRules.EffectiveStatus(job.Status, job.ExpiresAt, now);
            var link = $"/employer/jobs/{job.JobId}";

            if (effective == JobStatuses.Expired && job.ExpiresAt is { } expiredAt)
            {
                if (!_notifications.AlreadySent(employerId, NotificationTypes.JobExpired, expiredAt, link))
                    _notifications.Send(employerId, NotificationTypes.JobExpired, $"\"{job.Title}\" has expired and is no longer visible to job seekers.", link);

                return;
            }

            if (effective == JobStatuses.Active && job.ExpiresAt is { } exp && JobPostingRules.IsWithinExpiryWarningWindow(exp, now))
            {
                var windowStart = exp.AddDays(-JobPostingRules.ExpiryWarningDays);

                if (!_notifications.AlreadySent(employerId, NotificationTypes.JobExpiringSoon, windowStart, link))
                    _notifications.Send(employerId, NotificationTypes.JobExpiringSoon, $"\"{job.Title}\" expires in {JobPostingRules.ExpiryWarningDays} days. Renew it to keep it visible.", link);
            }
        }
    }
}
