namespace JobLinkv2.Services.Employer
{
    public static class JobPostPackages
    {
        public const string Single = "Single";
        public const string Bundle5 = "Bundle5";
        public const string Renewal = "Renewal";
    }

    // What a purchase actually buys: a Post credit (spent by Publish) or a Renewal
    // credit (spent by Renew). Single and Bundle5 both grant Post credits - they only
    // differ in how many, and at what price.
    public static class JobCreditKinds
    {
        public const string Post = "Post";
        public const string Renewal = "Renewal";
    }

    public static class JobStatuses
    {
        public const string Draft = "Draft";
        public const string Active = "Active";
        public const string Closed = "Closed";
        public const string Expired = "Expired";
    }

    public sealed record PostingPackageOption(string Package, string CreditKind, decimal PricePhp, int Credits, string Description);

    // Prices and credit grants are display/business facts, not something a client can send -
    // a purchase request names only the package, same as Subscriptions' PlanCatalogue.
    public static class JobPostCatalogue
    {
        public const int ActiveDays = 30;

        public static readonly IReadOnlyList<PostingPackageOption> Options = new[]
        {
            new PostingPackageOption(JobPostPackages.Single, JobCreditKinds.Post, 499m, 1,
                "One job post, active for 30 days"),
            new PostingPackageOption(JobPostPackages.Bundle5, JobCreditKinds.Post, 1999m, 5,
                "5 job post credits, each active for 30 days"),
            new PostingPackageOption(JobPostPackages.Renewal, JobCreditKinds.Renewal, 299m, 1,
                "Extends one post by 30 days"),
        };

        public static PostingPackageOption? Find(string? package) =>
            Options.FirstOrDefault(o => string.Equals(o.Package, package, StringComparison.OrdinalIgnoreCase));
    }

    // Pure rules over a single job row - no database access. Mirrors SubscriptionRules:
    // "checked at request time, no background job" for expiry, same as Premium.
    public static class JobPostingRules
    {
        public const int ExpiryWarningDays = 3;

        public const int MaxTitleLength = 150;
        public const int MaxCompanyLength = 150;
        public const int MaxLocationLength = 150;
        public const int MaxDescriptionLength = 8000;
        public const int MaxSkills = 30;

        public static readonly string[] WorkSetups = { "onsite", "remote", "hybrid" };
        public static readonly string[] JobTypes = { "FULLTIME", "PARTTIME", "CONTRACTOR", "INTERN" };

        // The status a row *should* show right now, given its stored status and expiry -
        // never written back here. Reads use this directly; a write path may also persist
        // it back onto the row when it happens to be there anyway (lazy, not scheduled).
        public static string EffectiveStatus(string storedStatus, DateTime? expiresAt, DateTime now) =>
            storedStatus == JobStatuses.Active && expiresAt is { } exp && exp <= now
                ? JobStatuses.Expired
                : storedStatus;

        public static bool IsWithinExpiryWarningWindow(DateTime expiresAt, DateTime now) =>
            now < expiresAt && expiresAt <= now.AddDays(ExpiryWarningDays);

        // First problem found with these fields, or null if they're fine to save. Shared by
        // create and edit so a draft can't be published into something invalid either.
        public static string? Validate(string? title, string? company, string? description, string? location,
            decimal? salaryMin, decimal? salaryMax, string? workSetup, string? jobType)
        {
            if (string.IsNullOrWhiteSpace(title)) return "Title is required.";
            if (title.Length > MaxTitleLength) return $"Title must be {MaxTitleLength} characters or fewer.";
            if (string.IsNullOrWhiteSpace(company)) return "Company is required.";
            if (company.Length > MaxCompanyLength) return $"Company must be {MaxCompanyLength} characters or fewer.";
            if (string.IsNullOrWhiteSpace(description)) return "Description is required.";
            if (description.Length > MaxDescriptionLength) return $"Description must be {MaxDescriptionLength} characters or fewer.";
            if (string.IsNullOrWhiteSpace(location)) return "Location is required.";
            if (location.Length > MaxLocationLength) return $"Location must be {MaxLocationLength} characters or fewer.";
            if (salaryMin is { } lo && lo < 0) return "Minimum salary can't be negative.";
            if (salaryMax is { } hi && hi < 0) return "Maximum salary can't be negative.";
            if (salaryMin is { } min && salaryMax is { } max && min > max) return "Minimum salary can't be more than maximum salary.";
            if (workSetup != null && !WorkSetups.Contains(workSetup)) return "Work setup must be onsite, remote, or hybrid.";
            if (jobType != null && !JobTypes.Contains(jobType)) return "Job type must be FULLTIME, PARTTIME, CONTRACTOR, or INTERN.";
            return null;
        }
    }
}
