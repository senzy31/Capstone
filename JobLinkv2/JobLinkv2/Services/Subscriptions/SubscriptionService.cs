using JobLinkv2.Services.Notifications;

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
        // "Expiring soon" starts this many days before premium_until - same window Task 1's job
        // posting expiry warning uses.
        public const int ExpiryWarningDays = 3;

        private const string PlansLink = "/DASHBOARD/Plans.html";

        private readonly ISubscriptionStore _store;
        private readonly TimeProvider _time;
        private readonly INotificationSender _notifications;

        public SubscriptionService(ISubscriptionStore store, TimeProvider time, INotificationSender notifications)
        {
            _store = store;
            _time = time;
            _notifications = notifications;
        }

        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        public PlanStatus GetStatus(int userId) => SubscriptionRules.StatusOf(_store.Get(userId), UtcNow);

        public bool IsPremium(int userId) => GetStatus(userId).IsPremium;

        // Simulated checkout: the plan is granted, nothing is charged.
        public PlanStatus Upgrade(int userId, BillingOption option)
        {
            var now = UtcNow;

            var saved = _store.Change(userId, current => SubscriptionRules.Upgrade(userId, current, option, now));

            var status = SubscriptionRules.StatusOf(saved, now);

            TrySend(userId, NotificationTypes.PremiumActivated,
                $"Premium activated! You're on the {status.Billing} plan until {status.Until:MMMM d, yyyy}.", PlansLink);

            return status;
        }

        public (CancelOutcome Outcome, PlanStatus Status) Cancel(int userId)
        {
            var now = UtcNow;
            var outcome = CancelOutcome.NoActivePlan;

            var saved = _store.Change(userId, current => SubscriptionRules.Cancel(current, now, out outcome));

            return (outcome, SubscriptionRules.StatusOf(saved, now));
        }

        // Checked only from GET /api/subscription - the one place a job seeker's own plan is
        // actively looked at - not from GetStatus/IsPremium, which nearly every plan-gated
        // action in the app calls; checking there would mean an extra Notifications query on
        // every one of those instead of once per plan page view. No background job: the next
        // person to check their plan after it crosses a threshold is the one who triggers it.
        public void CheckExpiryNotifications(int userId)
        {
            var record = _store.Get(userId);

            if (record is null)
                return;

            var now = UtcNow;
            var status = SubscriptionRules.StatusOf(record, now);

            if (status.IsPremium && status.Until is { } until && now < until && until <= now.AddDays(ExpiryWarningDays))
            {
                var windowStart = until.AddDays(-ExpiryWarningDays);

                if (!_notifications.AlreadySent(userId, NotificationTypes.PremiumExpiringSoon, windowStart))
                    TrySend(userId, NotificationTypes.PremiumExpiringSoon,
                        $"Your Premium plan expires on {until:MMMM d, yyyy}. Renew to keep your Premium features.", PlansLink);

                return;
            }

            // The row still says Premium (nothing rewrites it - see SubscriptionRules.StatusOf),
            // but its own until date has passed: this is a plan that just lapsed, not one that
            // was always Free.
            if (!status.IsPremium && record.Plan == PlanNames.Premium && record.Until is { } expiredAt && expiredAt <= now)
            {
                if (!_notifications.AlreadySent(userId, NotificationTypes.PremiumExpired, expiredAt))
                    TrySend(userId, NotificationTypes.PremiumExpired,
                        "Your Premium plan has expired. You're now on the Free plan.", PlansLink);
            }
        }

        // A notification failing must never break the action (or plan read) that triggered it.
        private void TrySend(int userId, string type, string message, string link)
        {
            try
            {
                _notifications.Send(userId, type, message, link);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
