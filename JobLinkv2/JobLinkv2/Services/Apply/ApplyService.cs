using JobLinkv2.Models;

namespace JobLinkv2.Services.Apply
{
    public abstract record ApplyResult;
    public sealed record ApplyJobNotFound : ApplyResult;
    public sealed record InternalApplied(int ApplicationId, string Status, bool AlreadyApplied) : ApplyResult;
    public sealed record ExternalRedirect(int ApplicationId, string RedirectUrl, string? Publisher, string Status) : ApplyResult;
    public sealed record ApplyExpired(string? Publisher) : ApplyResult;
    public sealed record ApplyRateLimited(int RetryAfterSeconds) : ApplyResult;

    public abstract record ConfirmResult;
    public sealed record ConfirmNotFound : ConfirmResult;
    public sealed record ConfirmForbidden : ConfirmResult;
    public sealed record ConfirmWrongStatus(string? CurrentStatus) : ConfirmResult;
    public sealed record ConfirmChanged(ApplicationModel Application) : ConfirmResult;
    public sealed record ConfirmUnchanged(ApplicationModel Application) : ConfirmResult;

    // The Apply button: internal jobs create an application an employer receives;
    // external jobs record the click and send the user to the original posting.
    public sealed class ApplyService
    {
        public const int InternalApplicationLimit = 20;

        public static readonly TimeSpan InternalApplicationWindow = TimeSpan.FromHours(24);

        private readonly IApplyStore _store;
        private readonly TimeProvider _time;

        public ApplyService(IApplyStore store, TimeProvider time)
        {
            _store = store;
            _time = time;
        }

        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        public ApplyResult Apply(int userId, int jobId)
        {
            var listing = _store.GetListing(jobId);

            if (listing is null)
                return new ApplyJobNotFound();

            return listing.Source == ListingSources.Internal
                ? ApplyInternal(userId, listing)
                : ApplyExternal(userId, listing);
        }

        // ------------------------------------------------------------------
        // Internal: a normal application the employer receives.
        // ------------------------------------------------------------------
        private ApplyResult ApplyInternal(int userId, JoblistingModel listing)
        {
            // Applying twice hands back the first application and costs nothing.
            var existing = _store.FindActiveApplication(userId, listing.JobId);

            if (existing != null)
                return AlreadyApplied(existing);

            var now = UtcNow;
            var windowStart = now - InternalApplicationWindow;

            var application = new ApplicationModel
            {
                UserId = userId,
                JobId = listing.JobId,
                ResumeId = _store.GetPrimaryResumeId(userId),
                Status = ApplicationStatuses.Submitted,
                ApplicationType = ApplicationTypes.Internal,
                AppliedAt = now
            };

            int? applicationId;

            try
            {
                applicationId = _store.TryAddInternalApplication(application, InternalApplicationLimit, windowStart);
            }
            catch (DuplicateApplicationException)
            {
                // A parallel request created it first.
                return AlreadyApplied(_store.FindActiveApplication(userId, listing.JobId)!);
            }

            if (applicationId is null)
            {
                var oldest = _store.GetOldestInternalApplicationSince(userId, windowStart) ?? now;

                var retryAfter = (int)Math.Ceiling((oldest + InternalApplicationWindow - now).TotalSeconds);

                return new ApplyRateLimited(Math.Max(retryAfter, 1));
            }

            NotifyEmployer(listing, userId);

            return new InternalApplied(applicationId.Value, ApplicationStatuses.Submitted, false);
        }

        private static InternalApplied AlreadyApplied(ApplicationModel existing) =>
            new(existing.ApplicationId, existing.Status ?? ApplicationStatuses.Submitted, true);

        // The application is already saved, so a failed notification must not fail the apply.
        private void NotifyEmployer(JoblistingModel listing, int applicantId)
        {
            if (listing.EmployerId is not int employerId)
                return;

            try
            {
                var applicant = _store.GetUserName(applicantId) ?? "A job seeker";

                _store.AddNotification(employerId, $"{applicant} applied for {listing.Title ?? "your job posting"}.");
            }
            catch
            {
                // Best effort.
            }
        }

        // ------------------------------------------------------------------
        // External: record the click, then hand back the original posting.
        // ------------------------------------------------------------------
        private ApplyResult ApplyExternal(int userId, JoblistingModel listing)
        {
            var link = ApplyLinkSelector.Choose(listing.ApplyUrl, listing.ApplyIsDirect, listing.ApplyOptions, listing.Publisher);

            if (link is null)
            {
                _store.MarkListingExpired(listing.JobId);

                return new ApplyExpired(listing.Publisher);
            }

            var existing = _store.FindActiveApplication(userId, listing.JobId);

            if (existing == null)
            {
                try
                {
                    var id = _store.AddApplication(new ApplicationModel
                    {
                        UserId = userId,
                        JobId = listing.JobId,
                        Status = ApplicationStatuses.Redirected,
                        ApplicationType = ApplicationTypes.External,
                        RedirectedAt = UtcNow
                    });

                    return new ExternalRedirect(id, link.Url, link.Publisher, ApplicationStatuses.Redirected);
                }
                catch (DuplicateApplicationException)
                {
                    existing = _store.FindActiveApplication(userId, listing.JobId);
                }
            }

            return new ExternalRedirect(
                existing!.ApplicationId,
                link.Url,
                link.Publisher,
                existing.Status ?? ApplicationStatuses.Redirected);
        }

        // ------------------------------------------------------------------
        // "Did you finish applying?"
        // ------------------------------------------------------------------
        public ConfirmResult ConfirmExternal(int userId, int applicationId, bool applied)
        {
            var application = _store.GetApplication(applicationId);

            if (application is null)
                return new ConfirmNotFound();

            if (application.UserId != userId)
                return new ConfirmForbidden();

            if (application.ApplicationType != ApplicationTypes.External ||
                application.Status != ApplicationStatuses.Redirected)
                return new ConfirmWrongStatus(application.Status);

            if (!applied)
                return new ConfirmUnchanged(application);

            var now = UtcNow;

            application.Status = ApplicationStatuses.AppliedExternally;
            application.ConfirmedAt = now;
            application.AppliedAt = now;

            _store.UpdateApplication(application);

            return new ConfirmChanged(application);
        }
    }
}
