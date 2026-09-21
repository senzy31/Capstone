namespace JobLinkv2.Services.Subscriptions
{
    public static class PlanNames
    {
        public const string Free = "Free";
        public const string Premium = "Premium";
    }

    // One way to pay for Premium. The price is for display only - payments are simulated.
    public sealed record BillingOption(string Billing, int Months, int PricePhp);

    public static class PlanCatalogue
    {
        public static readonly IReadOnlyList<BillingOption> Options = new[]
        {
            new BillingOption("Monthly", 1, 99),
            new BillingOption("Quarterly", 3, 249),
            new BillingOption("Annual", 12, 899)
        };

        public static BillingOption? Find(string? billing) =>
            Options.FirstOrDefault(o => string.Equals(o.Billing, billing?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // What the Subscriptions table holds for one user. No row means Free.
    public sealed record SubscriptionRecord(
        int UserId,
        string Plan,
        string? Billing,
        DateTime? StartedAt,
        DateTime? Until,
        DateTime? CancelledAt);

    // What a user's plan really is right now (see SubscriptionRules.StatusOf).
    public sealed record PlanStatus(
        bool IsPremium,
        string Plan,
        string? Billing,
        DateTime? StartedAt,
        DateTime? Until,
        bool Cancelled);

    // What each plan gets. Enforced on the server: a limit here is a limit the API
    // applies, not a button the page hides. Both plans keep the standard resume formats,
    // job listings, filters, recommendations and the overall suitability score, and both
    // get unlimited PDF downloads and the same 20-a-day internal application limit -
    // none of that is a plan setting, so none of it is here.
    public sealed record PlanLimits(
        int ResumeVersions,          // saved resumes
        int? SavedJobs,              // null = unlimited
        bool ShowAds,
        bool DetailedScore,          // the skills / location / salary sub-scores and matched skills
        bool MissingSkills,
        bool AdvancedTemplates,
        bool PriorityApplication)
    {
        public static readonly PlanLimits Free = new(
            ResumeVersions: 1, SavedJobs: 10, ShowAds: true,
            DetailedScore: false, MissingSkills: false, AdvancedTemplates: false, PriorityApplication: false);

        public static readonly PlanLimits Premium = new(
            ResumeVersions: 10, SavedJobs: null, ShowAds: false,
            DetailedScore: true, MissingSkills: true, AdvancedTemplates: true, PriorityApplication: true);

        public static PlanLimits For(bool isPremium) => isPremium ? Premium : Free;
    }

    // The plan rules as pure functions of (what is stored, what time it is), so they can be
    // tested without a database or a clock. All times are UTC.
    public static class SubscriptionRules
    {
        // Premium only while the plan says Premium AND premium_until is still ahead. Nothing
        // has to run when it passes: the next request that asks sees Free.
        public static PlanStatus StatusOf(SubscriptionRecord? record, DateTime nowUtc)
        {
            if (record is { Plan: PlanNames.Premium, Until: { } until } && until > nowUtc)
                return new PlanStatus(true, PlanNames.Premium, record.Billing, record.StartedAt, until, record.CancelledAt is not null);

            return new PlanStatus(false, PlanNames.Free, null, record?.StartedAt, record?.Until, false);
        }

        // Premium for the chosen period. While Premium is active the period is added after
        // premium_until (buying early never wastes time); otherwise it starts now. Upgrading
        // also takes back a cancellation.
        public static SubscriptionRecord Upgrade(int userId, SubscriptionRecord? current, BillingOption option, DateTime nowUtc)
        {
            var status = StatusOf(current, nowUtc);

            var startedAt = status.IsPremium ? status.StartedAt!.Value : nowUtc;
            var from = status.IsPremium ? status.Until!.Value : nowUtc;

            return new SubscriptionRecord(userId, PlanNames.Premium, option.Billing, startedAt, from.AddMonths(option.Months), null);
        }

        // Cancelling keeps Premium until premium_until; it only stops it renewing (there is no
        // renewal in this demo, so it is a marker the page can show). Null = nothing to change.
        public static SubscriptionRecord? Cancel(SubscriptionRecord? current, DateTime nowUtc, out CancelOutcome outcome)
        {
            if (!StatusOf(current, nowUtc).IsPremium)
            {
                outcome = CancelOutcome.NoActivePlan;
                return null;
            }

            if (current!.CancelledAt is not null)
            {
                outcome = CancelOutcome.AlreadyCancelled;
                return null;
            }

            outcome = CancelOutcome.Cancelled;
            return current with { CancelledAt = nowUtc };
        }
    }

    public enum CancelOutcome
    {
        Cancelled,
        AlreadyCancelled,
        NoActivePlan
    }
}
