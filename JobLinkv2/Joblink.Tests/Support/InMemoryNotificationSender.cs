using JobLinkv2.Services.Notifications;

namespace Joblink.Tests.Support
{
    public sealed record SentNotification(int UserId, string Type, string Message, string? Link, DateTime SentAtUtc);

    // An in-memory INotificationSender: no test needs a real Notifications row to check that the
    // right message went to the right user, and this keeps every test off the real database.
    public sealed class InMemoryNotificationSender : INotificationSender
    {
        private readonly List<SentNotification> _sent = new();
        private readonly object _gate = new();

        public IReadOnlyList<SentNotification> Sent
        {
            get { lock (_gate) return _sent.ToList(); }
        }

        public void Send(int userId, string type, string message, string? link = null)
        {
            lock (_gate)
                _sent.Add(new SentNotification(userId, type, message, link, DateTime.UtcNow));
        }

        public bool AlreadySent(int userId, string type, DateTime sinceUtc, string? link = null)
        {
            lock (_gate)
                return _sent.Any(n =>
                    n.UserId == userId && n.Type == type && n.SentAtUtc >= sinceUtc && (link == null || n.Link == link));
        }
    }
}
