using System.Text.Json;
using Joblink.Services.JobSearch;
using JobLinkv2.Services.Matching;

namespace Joblink.Tests.Support
{
    // A job feed the tests control: no network, no RapidAPI. Records every search it is asked for.
    public sealed class FakeJobSearchService : IJobSearchService
    {
        private readonly object _gate = new();
        private JsonElement _jobs = JsonSerializer.SerializeToElement(Array.Empty<object>());
        private UpstreamResult<JsonElement>? _failure;

        public List<(string Query, int Page)> Searches { get; } = new();

        public string? LastQuery
        {
            get { lock (_gate) return Searches.Count == 0 ? null : Searches[^1].Query; }
        }

        // Every search answers with these jobs (plain objects with JSearch's snake_case names).
        public void Returns(params object[] jobs)
        {
            lock (_gate)
            {
                _jobs = JsonSerializer.SerializeToElement(jobs);
                _failure = null;
            }
        }

        // Every search fails the way the real service reports a failure.
        public void Fails(int status, string message)
        {
            lock (_gate)
                _failure = UpstreamResult<JsonElement>.Failure(status, message);
        }

        public Task<UpstreamResult<JsonElement>> SearchAsync(string query, int page, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Searches.Add((query, page));

                return Task.FromResult(_failure ?? UpstreamResult<JsonElement>.Success(_jobs));
            }
        }

        public Task<UpstreamResult<JsonElement>> DetailsAsync(string jobId, CancellationToken cancellationToken) =>
            Task.FromResult(UpstreamResult<JsonElement>.Success(JsonSerializer.SerializeToElement(new { status = "OK", data = Array.Empty<object>() })));

        public Task<UpstreamResult<JsonElement>> SalaryAsync(string jobTitle, string location, CancellationToken cancellationToken) =>
            Task.FromResult(UpstreamResult<JsonElement>.Success(JsonSerializer.SerializeToElement(new { status = "OK", data = Array.Empty<object>() })));
    }

    // Job seekers' resumes and preferences, without a database: whoever is not set has no skills.
    public sealed class InMemoryScoringProfileReader : IScoringProfileReader
    {
        private readonly Dictionary<int, ScoringProfile> _profiles = new();
        private readonly object _gate = new();

        public List<int> Reads { get; } = new();

        public void Set(int userId, ScoringProfile profile)
        {
            lock (_gate)
                _profiles[userId] = profile;
        }

        public ScoringProfile Read(int userId)
        {
            lock (_gate)
            {
                Reads.Add(userId);

                return _profiles.TryGetValue(userId, out var profile) ? profile : new ScoringProfile(Array.Empty<string>());
            }
        }
    }
}
