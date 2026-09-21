using JobLinkv2.Services.Subscriptions;

namespace Joblink.Tests.Support
{
    // Who is on Premium, for tests that only need the answer. Counts how often it was asked, and
    // can be told to fail (a plan that can't be read).
    public sealed class FakePlanReader : IPlanReader
    {
        private readonly HashSet<int> _premium = new();
        private int _asked;

        public int Asked => _asked;

        public bool Broken { get; set; }

        public void MakePremium(int userId) => _premium.Add(userId);

        public void MakeFree(int userId) => _premium.Remove(userId);

        public bool IsPremium(int userId)
        {
            Interlocked.Increment(ref _asked);

            if (Broken)
                throw new InvalidOperationException("The subscription table is unreachable.");

            return _premium.Contains(userId);
        }
    }
}
