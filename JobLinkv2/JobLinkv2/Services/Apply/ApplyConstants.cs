namespace JobLinkv2.Services.Apply
{
    public static class ListingSources
    {
        public const string Internal = "Internal";
        public const string External = "External";

        // source_api value for jobs imported from JSearch, and for jobs a user
        // logged by hand in the application tracker.
        public const string JSearchApi = "jsearch";
        public const string ManualApi = "manual";
    }

    public static class ApplicationTypes
    {
        public const string Internal = "Internal";
        public const string External = "External";
    }

    public static class ApplicationStatuses
    {
        // Internal (an employer receives the application)
        public const string Submitted = "Submitted";
        public const string Viewed = "Viewed";
        public const string Shortlisted = "Shortlisted";
        public const string Rejected = "Rejected";

        // External (the user was sent to the original posting)
        public const string Redirected = "Redirected";
        public const string AppliedExternally = "Applied Externally";

        // The tracker's own statuses for applications the user logs by hand.
        public static readonly string[] Manual =
            { "Applied", "Under Review", "Interview", "Offer", "Rejected" };

        public static bool IsManual(string? status) =>
            status != null && Manual.Contains(status);
    }
}
