using JobLinkv2.Models;

namespace JobLinkv2.Services.Apply
{
    public enum TrackerOutcome
    {
        Ok,
        NotFound,
        Forbidden,
        Invalid,
        Conflict,
        Managed
    }

    public sealed record TrackerResult<T>(TrackerOutcome Outcome, T? Value = default, string? Message = null)
    {
        public bool IsOk => Outcome == TrackerOutcome.Ok;
    }

    // The application tracker's own create / change-status / withdraw actions.
    //
    // These endpoints used to accept any application row from any caller, which
    // let a client forge internal applications, skip the apply limit and rewrite
    // statuses the apply flow owns. Now the caller must own the application, the
    // user id always comes from the login token, and only the tracker's manual
    // statuses can be set by hand - Submitted/Viewed/Shortlisted are the
    // employer's, Redirected/Applied Externally belong to the redirect flow.
    public sealed class ApplicationTrackerService
    {
        private const int MaxTextLength = 300;

        private readonly IApplyStore _store;
        private readonly TimeProvider _time;

        public ApplicationTrackerService(IApplyStore store, TimeProvider time)
        {
            _store = store;
            _time = time;
        }

        public IReadOnlyList<ApplicationModel> List(int userId) =>
            _store.GetApplicationsByUser(userId);

        public TrackerResult<ApplicationModel> Get(int userId, int applicationId)
        {
            var application = _store.GetApplication(applicationId);

            if (application is null)
                return new(TrackerOutcome.NotFound);

            if (application.UserId != userId)
                return new(TrackerOutcome.Forbidden, Message: "That application belongs to another user.");

            return new(TrackerOutcome.Ok, application);
        }

        // "Log Application": a job the user typed in by hand.
        public TrackerResult<JoblistingModel> LogManualListing(JoblistingModel input)
        {
            var title = input.Title?.Trim();
            var company = input.Company?.Trim();
            var location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(company))
                return new(TrackerOutcome.Invalid, Message: "A job title and company are required.");

            if (title.Length > MaxTextLength || company.Length > MaxTextLength || (location?.Length ?? 0) > MaxTextLength)
                return new(TrackerOutcome.Invalid, Message: "Title, company and location can be at most 300 characters.");

            // Everything that decides where Apply sends people (apply_url, options,
            // publisher, source, employer) is set by the server, never the client.
            var listing = _store.AddManualListing(new JoblistingModel
            {
                ExternalJobId = $"manual-{Guid.NewGuid():N}",
                Title = title,
                Company = company,
                Location = location,
                SourceApi = ListingSources.ManualApi,
                Source = ListingSources.External
            });

            return new(TrackerOutcome.Ok, listing);
        }

        public TrackerResult<ApplicationModel> LogManual(int userId, ApplicationModel input)
        {
            if (!ApplicationStatuses.IsManual(input.Status))
                return new(TrackerOutcome.Invalid, Message: $"Status must be one of: {string.Join(", ", ApplicationStatuses.Manual)}.");

            var listing = _store.GetListing(input.JobId);

            if (listing is null || listing.SourceApi != ListingSources.ManualApi)
                return new(TrackerOutcome.Invalid, Message: "You can only log applications for jobs you added by hand.");

            var application = new ApplicationModel
            {
                UserId = userId,
                JobId = listing.JobId,
                ResumeId = _store.GetPrimaryResumeId(userId),
                Status = input.Status,
                ApplicationType = ApplicationTypes.External,
                AppliedAt = input.AppliedAt ?? _time.GetUtcNow().UtcDateTime
            };

            try
            {
                application.ApplicationId = _store.AddApplication(application);
            }
            catch (DuplicateApplicationException)
            {
                return new(TrackerOutcome.Conflict, Message: "You already logged an application for this job.");
            }

            return new(TrackerOutcome.Ok, application);
        }

        // Only the status can change, and only between the tracker's own statuses.
        public TrackerResult<ApplicationModel> ChangeStatus(int userId, int applicationId, string? newStatus)
        {
            var found = Get(userId, applicationId);

            if (!found.IsOk)
                return found;

            var application = found.Value!;

            if (application.ApplicationType != ApplicationTypes.External || !ApplicationStatuses.IsManual(application.Status))
                return new(TrackerOutcome.Managed, Message: "This application's status is managed by JobLink and can't be changed here.");

            if (!ApplicationStatuses.IsManual(newStatus))
                return new(TrackerOutcome.Invalid, Message: $"Status must be one of: {string.Join(", ", ApplicationStatuses.Manual)}.");

            application.Status = newStatus;

            _store.UpdateApplication(application);

            return new(TrackerOutcome.Ok, application);
        }

        // Withdrawing is a soft delete. Internal applications still count toward
        // the 24-hour apply limit afterwards, so apply-withdraw-apply can't dodge it.
        public TrackerResult<bool> Withdraw(int userId, int applicationId)
        {
            var found = Get(userId, applicationId);

            if (!found.IsOk)
                return new(found.Outcome, Message: found.Message);

            return new(TrackerOutcome.Ok, _store.SoftDeleteApplication(applicationId));
        }
    }
}
