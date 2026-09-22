using JobLinkv2.Models;

namespace JobLinkv2.Services.Apply
{
    // A job as imported from JSearch (see JSearchListingMapper).
    public sealed record ImportedListing(
        string ExternalJobId,
        string? Title,
        string? Company,
        string? Location,
        string? Description,
        DateTime? PostedDate,
        string? ApplyUrl,
        bool ApplyIsDirect,
        string? Publisher,
        string? ApplyOptionsJson,
        decimal? Latitude,
        decimal? Longitude);

    // Thrown when the user already has a live application for the job
    // (the unique index on Applications caught a concurrent duplicate).
    public sealed class DuplicateApplicationException : Exception
    {
        public DuplicateApplicationException() : base("The user already has an application for this job.") { }
    }

    // Everything the apply flow needs from the database. Every read ignores
    // soft-deleted rows (is_deleted = 1) unless noted.
    public interface IApplyStore
    {
        // ----- job listings -------------------------------------------------
        JoblistingModel? GetListing(int jobId);

        void MarkListingExpired(int jobId);

        // Inserts the job, or updates it if it was imported before. Returns its job_id.
        int UpsertExternalListing(ImportedListing listing);

        // A job the user typed into the tracker by hand. Returns it with its new job_id.
        JoblistingModel AddManualListing(JoblistingModel listing);

        // ----- applications -------------------------------------------------
        ApplicationModel? GetApplication(int applicationId);

        IReadOnlyList<ApplicationModel> GetApplicationsByUser(int userId);

        ApplicationModel? FindActiveApplication(int userId, int jobId);

        // Throws DuplicateApplicationException if the user already has one.
        int AddApplication(ApplicationModel application);

        // Inserts an Internal application only if the user has fewer than `limit`
        // Internal applications since `windowStartUtc` - checked and inserted in
        // one atomic step so parallel requests can't slip past the limit.
        // Soft-deleted applications still count. Returns null when over the limit.
        int? TryAddInternalApplication(ApplicationModel application, int limit, DateTime windowStartUtc);

        // Earliest Internal application (deleted ones included) since the window
        // start - used to tell the user when they can apply again.
        DateTime? GetOldestInternalApplicationSince(int userId, DateTime windowStartUtc);

        // Saves status, applied_at, redirected_at, confirmed_at and resume_id.
        void UpdateApplication(ApplicationModel application);

        bool SoftDeleteApplication(int applicationId);

        // ----- people -------------------------------------------------------
        int? GetPrimaryResumeId(int userId);

        string? GetUserName(int userId);
    }
}
