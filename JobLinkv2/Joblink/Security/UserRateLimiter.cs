using System.Collections.Concurrent;

namespace Joblink.Security
{
    public readonly record struct RateDecision(bool Allowed, TimeSpan RetryAfter);

    // "At most N per rolling window" for one key (for example one user's AI requests).
    // Kept in memory, so it is per running server - fine for one instance; several
    // instances would each allow the full amount. Uses the injected clock so tests can
    // move time forward instead of waiting.
    public sealed class UserRateLimiter
    {
        private readonly TimeProvider _time;
        private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

        public UserRateLimiter(TimeProvider time)
        {
            _time = time;
        }

        // Counts this request if it is allowed. When it isn't, RetryAfter is how long until
        // the oldest counted request leaves the window and a slot frees up.
        public RateDecision TryTake(string key, int limit, TimeSpan window)
        {
            var now = _time.GetUtcNow();
            var hits = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());

            lock (hits)
            {
                while (hits.Count > 0 && now - hits.Peek() >= window)
                    hits.Dequeue();

                if (hits.Count >= limit)
                    return new RateDecision(false, hits.Peek() + window - now);

                hits.Enqueue(now);

                return new RateDecision(true, TimeSpan.Zero);
            }
        }
    }
}
