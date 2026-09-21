using JobLinkv2.Models;
using JobLinkv2.Services.Apply;

namespace Joblink.Tests.Support
{
    // An in-memory IApplyStore that follows the same rules as SqlApplyStore:
    // soft-deleted rows are hidden from reads, one live application per user and
    // job, and the internal-application limit counts soft-deleted rows too.
    public sealed class InMemoryApplyStore : IApplyStore
    {
        public List<JoblistingModel> Listings { get; } = new();
        public List<ApplicationModel> Applications { get; } = new();
        public List<(int UserId, string Message)> Notifications { get; } = new();
        public Dictionary<int, string> UserNames { get; } = new();
        public Dictionary<int, int> PrimaryResumes { get; } = new();

        // Set to make the next insert lose a "race": another request's application
        // for the same user and job appears first, so the insert hits the unique index.
        public bool LoseNextInsertRace { get; set; }

        public bool NotificationsFail { get; set; }

        private int _nextListingId = 1;
        private int _nextApplicationId = 1;

        // ----- test helpers -----------------------------------------------------

        public JoblistingModel AddListing(JoblistingModel listing)
        {
            listing.JobId = _nextListingId++;
            Listings.Add(listing);
            return listing;
        }

        public JoblistingModel AddInternalJob(int employerId, string title = "Backend Developer") =>
            AddListing(new JoblistingModel
            {
                Title = title,
                Company = "Acme Corp",
                SourceApi = "employer",
                Source = ListingSources.Internal,
                EmployerId = employerId
            });

        public JoblistingModel AddExternalJob(
            string? applyUrl = "https://www.linkedin.com/jobs/view/123",
            bool applyIsDirect = false,
            string? applyOptionsJson = null,
            string? publisher = "LinkedIn",
            string title = "QA Engineer") =>
            AddListing(new JoblistingModel
            {
                Title = title,
                Company = "Globex",
                SourceApi = ListingSources.JSearchApi,
                Source = ListingSources.External,
                ApplyUrl = applyUrl,
                ApplyIsDirect = applyIsDirect,
                ApplyOptions = applyOptionsJson,
                Publisher = publisher
            });

        public ApplicationModel AddApplicationRow(ApplicationModel application)
        {
            application.ApplicationId = _nextApplicationId++;
            Applications.Add(application);
            return application;
        }

        // ----- IApplyStore ------------------------------------------------------

        public JoblistingModel? GetListing(int jobId) =>
            Listings.FirstOrDefault(l => l.JobId == jobId && !l.IsDeleted);

        public void MarkListingExpired(int jobId)
        {
            var listing = GetListing(jobId);

            if (listing != null)
                listing.IsExpired = true;
        }

        public int UpsertExternalListing(ImportedListing imported)
        {
            var existing = Listings.FirstOrDefault(l =>
                l.SourceApi == ListingSources.JSearchApi && l.ExternalJobId == imported.ExternalJobId && !l.IsDeleted);

            var listing = existing ?? new JoblistingModel
            {
                ExternalJobId = imported.ExternalJobId,
                SourceApi = ListingSources.JSearchApi,
                Source = ListingSources.External
            };

            listing.Title = imported.Title;
            listing.Company = imported.Company;
            listing.Location = imported.Location;
            listing.Description = imported.Description;
            listing.PostedDate = imported.PostedDate;
            listing.ApplyUrl = imported.ApplyUrl;
            listing.ApplyIsDirect = imported.ApplyIsDirect;
            listing.Publisher = imported.Publisher;
            listing.ApplyOptions = imported.ApplyOptionsJson;
            listing.Latitude = imported.Latitude;
            listing.Longitude = imported.Longitude;

            if (ApplyLinkSelector.Choose(imported.ApplyUrl, imported.ApplyIsDirect, imported.ApplyOptionsJson) != null)
                listing.IsExpired = false;

            return existing?.JobId ?? AddListing(listing).JobId;
        }

        public JoblistingModel AddManualListing(JoblistingModel listing)
        {
            listing.Source = ListingSources.External;
            listing.EmployerId = null;
            return AddListing(listing);
        }

        public ApplicationModel? GetApplication(int applicationId) =>
            Applications.FirstOrDefault(a => a.ApplicationId == applicationId && !a.IsDeleted);

        public IReadOnlyList<ApplicationModel> GetApplicationsByUser(int userId) =>
            Applications.Where(a => a.UserId == userId && !a.IsDeleted).OrderBy(a => a.ApplicationId).ToList();

        public ApplicationModel? FindActiveApplication(int userId, int jobId) =>
            Applications.FirstOrDefault(a => a.UserId == userId && a.JobId == jobId && !a.IsDeleted);

        public int AddApplication(ApplicationModel application)
        {
            if (LoseNextInsertRace)
            {
                LoseNextInsertRace = false;

                AddApplicationRow(new ApplicationModel
                {
                    UserId = application.UserId,
                    JobId = application.JobId,
                    Status = application.ApplicationType == ApplicationTypes.Internal
                        ? ApplicationStatuses.Submitted
                        : ApplicationStatuses.Redirected,
                    ApplicationType = application.ApplicationType,
                    AppliedAt = application.AppliedAt,
                    RedirectedAt = application.RedirectedAt
                });
            }

            if (FindActiveApplication(application.UserId, application.JobId) != null)
                throw new DuplicateApplicationException();

            return AddApplicationRow(Clone(application)).ApplicationId;
        }

        public int? TryAddInternalApplication(ApplicationModel application, int limit, DateTime windowStartUtc)
        {
            var recent = Applications.Count(a =>
                a.UserId == application.UserId &&
                a.ApplicationType == ApplicationTypes.Internal &&
                a.AppliedAt >= windowStartUtc);

            if (recent >= limit)
                return null;

            return AddApplication(application);
        }

        public DateTime? GetOldestInternalApplicationSince(int userId, DateTime windowStartUtc) =>
            Applications
                .Where(a => a.UserId == userId && a.ApplicationType == ApplicationTypes.Internal && a.AppliedAt >= windowStartUtc)
                .Select(a => a.AppliedAt)
                .Min();

        public void UpdateApplication(ApplicationModel application)
        {
            var stored = Applications.First(a => a.ApplicationId == application.ApplicationId && !a.IsDeleted);

            stored.Status = application.Status;
            stored.AppliedAt = application.AppliedAt;
            stored.RedirectedAt = application.RedirectedAt;
            stored.ConfirmedAt = application.ConfirmedAt;
            stored.ResumeId = application.ResumeId;
        }

        public bool SoftDeleteApplication(int applicationId)
        {
            var stored = GetApplication(applicationId);

            if (stored is null)
                return false;

            stored.IsDeleted = true;
            return true;
        }

        public int? GetPrimaryResumeId(int userId) =>
            PrimaryResumes.TryGetValue(userId, out var id) ? id : null;

        public string? GetUserName(int userId) =>
            UserNames.TryGetValue(userId, out var name) ? name : null;

        public void AddNotification(int userId, string message)
        {
            if (NotificationsFail)
                throw new InvalidOperationException("notifications are down");

            Notifications.Add((userId, message));
        }

        private static ApplicationModel Clone(ApplicationModel a) => new()
        {
            UserId = a.UserId,
            JobId = a.JobId,
            ResumeId = a.ResumeId,
            Status = a.Status,
            AppliedAt = a.AppliedAt,
            ApplicationType = a.ApplicationType,
            RedirectedAt = a.RedirectedAt,
            ConfirmedAt = a.ConfirmedAt
        };
    }
}
