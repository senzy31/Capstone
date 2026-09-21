namespace Joblink.Tests.Support
{
    // A clock the tests can move forward, so "24 hours later" doesn't mean waiting.
    public sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;

        public TestClock(string startUtc = "2026-09-21T08:00:00Z")
        {
            _now = DateTimeOffset.Parse(startUtc);
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public DateTime UtcNow => _now.UtcDateTime;

        public void Advance(TimeSpan by) => _now += by;
    }
}
