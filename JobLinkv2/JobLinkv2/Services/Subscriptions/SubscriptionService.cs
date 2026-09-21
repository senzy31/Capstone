namespace JobLinkv2.Services.Subscriptions
{
    // Reads a user's plan. Everything that behaves differently by plan asks this, on every
    // request, and it asks the database - never the login token - so an upgrade or an expiry
    // takes effect immediately, with the same token.
    public interface IPlanReader
    {
        bool IsPremium(int userId);
    }

    // Everything the subscription code needs from the database.
    public interface ISubscriptionStore
    {
        SubscriptionRecord? Get(int userId);

        // Reads the user's row, lets `change` decide the new one (null = leave it), and saves
        // it - as one step per user, so two parallel upgrades both count. Returns the row as it
        // is afterwards. `change` may run more than once (a retry), so it must not have effects.
        SubscriptionRecord? Change(int userId, Func<SubscriptionRecord?, SubscriptionRecord?> change);
    }

    public sealed class SubscriptionService : IPlanReader
    {
        private readonly ISubscriptionStore _store;
        private readonly TimeProvider _time;

        public SubscriptionService(ISubscriptionStore store, TimeProvider time)
        {
            _store = store;
            _time = time;
        }

        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        public PlanStatus GetStatus(int userId) => SubscriptionRules.StatusOf(_store.Get(userId), UtcNow);

        public bool IsPremium(int userId) => GetStatus(userId).IsPremium;

        // Simulated checkout: the plan is granted, nothing is charged.
        public PlanStatus Upgrade(int userId, BillingOption option)
        {
            var now = UtcNow;

            var saved = _store.Change(userId, current => SubscriptionRules.Upgrade(userId, current, option, now));

            return SubscriptionRules.StatusOf(saved, now);
        }

        public (CancelOutcome Outcome, PlanStatus Status) Cancel(int userId)
        {
            var now = UtcNow;
            var outcome = CancelOutcome.NoActivePlan;

            var saved = _store.Change(userId, current => SubscriptionRules.Cancel(current, now, out outcome));

            return (outcome, SubscriptionRules.StatusOf(saved, now));
        }
    }
}
