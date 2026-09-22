using System.ComponentModel.DataAnnotations;
using JobLinkv2.Services.Employer;

namespace Joblink.Services.Employer
{
    // Which package to buy. The price, credits and package shape are decided by the server
    // (JobPostCatalogue) - a request only names one, same as Subscriptions' UpgradeRequest.
    public sealed class PurchaseRequest
    {
        [Required] public string? Package { get; set; }
    }

    // Create and edit share every field a client may set. The owner (employer id, from the
    // JWT), status and dates are never here - only EmployerJobService/EmployerJobStore decide
    // those, through Publish/Close/Renew.
    public sealed class JobRequest
    {
        [Required] public string? Title { get; set; }
        [Required] public string? Company { get; set; }
        [Required] public string? Description { get; set; }
        [Required] public string? Location { get; set; }
        public decimal? SalaryMin { get; set; }
        public decimal? SalaryMax { get; set; }
        public string? WorkSetup { get; set; }
        public string? JobType { get; set; }
        public List<string>? Skills { get; set; }
    }

    public sealed record PostingPackageView(string Package, string CreditKind, decimal PricePhp, int Credits, string Description);

    public sealed record CreditsView(int PostCredits, int RenewalCredits);

    public sealed record PurchaseView(string Package, decimal AmountPhp, int CreditsGranted, DateTime PurchasedAt, CreditsView Balance);

    public sealed record EmployerJobView(
        int JobId, string Title, string Company, string Description, string Location,
        decimal? SalaryMin, decimal? SalaryMax, string? WorkSetup, string? JobType, string Status,
        DateTime? PublishedAt, DateTime? ExpiresAt, int? DaysLeft, int ApplicantCount, IReadOnlyList<string> Skills)
    {
        public static EmployerJobView From(JoblistingModelWithSkills job, DateTime now)
        {
            var effectiveStatus = JobPostingRules.EffectiveStatus(job.Job.Status, job.Job.ExpiresAt, now);

            int? daysLeft = effectiveStatus == JobStatuses.Active && job.Job.ExpiresAt is { } exp
                ? Math.Max(0, (int)Math.Ceiling((exp - now).TotalDays))
                : null;

            return new EmployerJobView(
                job.Job.JobId, job.Job.Title ?? "", job.Job.Company ?? "", job.Job.Description ?? "", job.Job.Location ?? "",
                job.Job.SalaryMin, job.Job.SalaryMax, job.Job.WorkSetup, job.Job.JobType, effectiveStatus,
                job.Job.PublishedAt, job.Job.ExpiresAt, daysLeft, job.ApplicantCount, job.Skills);
        }
    }

    // A job plus the two things its view needs that the row alone doesn't carry.
    public sealed record JoblistingModelWithSkills(JobLinkv2.Models.JoblistingModel Job, int ApplicantCount, IReadOnlyList<string> Skills);
}
