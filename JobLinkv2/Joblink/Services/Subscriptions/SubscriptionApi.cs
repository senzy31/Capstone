using System.ComponentModel.DataAnnotations;
using JobLinkv2.Services.Subscriptions;

namespace Joblink.Services.Subscriptions
{
    // The only thing a client sends to change its plan: which billing period it "bought".
    // (Payments are simulated. There is no plan, expiry date or price in a request - those
    // are decided by the server.)
    public sealed class UpgradeRequest
    {
        [Required] public string? Billing { get; set; }
    }

    // Demo checkout is the one sanctioned way for a user to grant themselves Premium. This
    // switch (Subscription:DemoCheckout, on by default) turns that path off - it has to be
    // off before any real payment provider is wired in.
    public sealed class SubscriptionOptions
    {
        public bool DemoCheckout { get; set; } = true;
    }

    public sealed record LimitsView(int ResumeVersions, int? SavedJobs);

    public sealed record UsageView(int ResumeVersions, int SavedJobs);

    public sealed record FeaturesView(
        bool ShowAds,
        bool DetailedScore,
        bool MissingSkills,
        bool AdvancedTemplates,
        bool PriorityApplication);

    public sealed record PlanOptionView(string Billing, int Months, int PricePhp);

    // What GET /api/subscription (and upgrade / cancel) answer: the plan as it really is
    // right now, what it allows, and how much of that has been used.
    public sealed record SubscriptionResponse(
        string Plan,
        bool IsPremium,
        string? Billing,
        DateTime? PremiumStartedAt,
        DateTime? PremiumUntil,
        bool Cancelled,
        LimitsView Limits,
        UsageView Usage,
        FeaturesView Features,
        IReadOnlyList<PlanOptionView> Plans,
        bool DemoCheckout)
    {
        public static SubscriptionResponse From(PlanStatus status, UsageView usage, bool demoCheckout)
        {
            var limits = PlanLimits.For(status.IsPremium);

            return new SubscriptionResponse(
                status.Plan,
                status.IsPremium,
                status.Billing,
                Utc(status.StartedAt),
                Utc(status.Until),
                status.Cancelled,
                new LimitsView(limits.ResumeVersions, limits.SavedJobs),
                usage,
                new FeaturesView(limits.ShowAds, limits.DetailedScore, limits.MissingSkills, limits.AdvancedTemplates, limits.PriorityApplication),
                PlanCatalogue.Options.Select(o => new PlanOptionView(o.Billing, o.Months, o.PricePhp)).ToList(),
                demoCheckout);
        }

        // The database holds UTC without a time zone; say so, so browsers don't read it as local time.
        private static DateTime? Utc(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
    }
}
