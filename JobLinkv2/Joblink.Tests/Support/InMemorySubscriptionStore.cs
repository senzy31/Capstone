using JobLinkv2.Services.Subscriptions;

namespace Joblink.Tests.Support
{
    // An in-memory ISubscriptionStore that keeps the real store's promise: reading, deciding
    // and writing one user's row happens as one step, so parallel changes don't lose each other.
    public sealed class InMemorySubscriptionStore : ISubscriptionStore
    {
        private readonly Dictionary<int, SubscriptionRecord> _rows = new();
        private readonly object _gate = new();

        public SubscriptionRecord? Get(int userId)
        {
            lock (_gate)
                return _rows.TryGetValue(userId, out var row) ? row : null;
        }

        public SubscriptionRecord? Change(int userId, Func<SubscriptionRecord?, SubscriptionRecord?> change)
        {
            lock (_gate)
            {
                _rows.TryGetValue(userId, out var current);

                var next = change(current);

                if (next is null)
                    return current;

                _rows[userId] = next;

                return next;
            }
        }

        // For tests that need a user to be Premium (or lapsed) without going through checkout.
        public void Set(SubscriptionRecord record)
        {
            lock (_gate)
                _rows[record.UserId] = record;
        }
    }

    // The counts GET /api/subscription reports, without a database.
    public sealed class FakeUsageReader : IUsageReader
    {
        private readonly Dictionary<int, PlanUsage> _usage = new();

        public void Set(int userId, int resumes, int savedJobs) => _usage[userId] = new PlanUsage(resumes, savedJobs);

        public PlanUsage For(int userId) => _usage.TryGetValue(userId, out var usage) ? usage : new PlanUsage(0, 0);
    }
}
