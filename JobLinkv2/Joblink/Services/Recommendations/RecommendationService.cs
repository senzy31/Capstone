using System.Text.Json;
using System.Text.Json.Nodes;
using Joblink.Services.JobSearch;
using JobLinkv2.Services.Matching;
using JobLinkv2.Services.Subscriptions;

namespace Joblink.Services.Recommendations
{
    public abstract record RecommendationOutcome;

    // The jobs, best match first, each with its match (joblink_match) already cut down to what the plan may see.
    public sealed record Recommended(string? Query, int Page, int SkillCount, bool HasPreferences, bool Detailed, JsonArray Jobs) : RecommendationOutcome;

    // The job search failed; Status and Message are fit to hand to the client as they are.
    public sealed record RecommendationFailed(int Status, string Message) : RecommendationOutcome;

    // A job seeker's recommended jobs: search for jobs like the ones on their resume, score every one against
    // them, put the best first, and show each score as much of itself as their plan allows.
    //
    // Everything about the person - skills, preferences, plan - is read here from the database by their id.
    // Nothing they send can change what they are scored as or which plan's view they get, and the sub-scores
    // only exist in the response for a plan that may see them (see MatchPresenter).
    public sealed class RecommendationService
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private readonly IScoringProfileReader _profiles;
        private readonly IJobSearchService _search;
        private readonly IPlanReader _plans;
        private readonly ILogger<RecommendationService> _logger;

        public RecommendationService(IScoringProfileReader profiles, IJobSearchService search, IPlanReader plans, ILogger<RecommendationService> logger)
        {
            _profiles = profiles;
            _search = search;
            _plans = plans;
            _logger = logger;
        }

        public async Task<RecommendationOutcome> GetAsync(int userId, int page, CancellationToken cancellationToken)
        {
            var profile = _profiles.Read(userId);

            var hasPreferences = profile.Preferences is { } p &&
                (!string.IsNullOrEmpty(p.PreferredLocation) || !string.IsNullOrEmpty(p.WorkArrangement) || p.MinSalary != 0 || p.MaxSalary != 0);

            var detailed = ShowsDetail(userId);

            // Nothing to match on: say so without spending a search.
            if (profile.Skills.Count == 0)
                return new Recommended(null, page, 0, hasPreferences, detailed, new JsonArray());

            var query = RecommendationQuery.Build(profile);

            var found = await _search.SearchAsync(query, page, cancellationToken);

            if (!found.Ok)
                return new RecommendationFailed(found.Status, found.Message!);

            var scored = new List<(JsonObject Node, int Score, int Matched)>();

            foreach (var element in found.Value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                var job = ScoringJob.FromJson(element);
                var result = SuitabilityScorer.Score(job, profile);

                var match = MatchPresenter.For(result, job, profile, detailed);

                // A copy: what the search returns is cached and shared, and must not be changed.
                var node = JsonNode.Parse(element.GetRawText())!.AsObject();
                node["joblink_match"] = JsonSerializer.SerializeToNode(match, match.GetType(), Web);

                scored.Add((node, result.Score, result.Skills?.Matched.Count ?? 0));
            }

            // Best match first; mentioning more of their skills breaks a tie; otherwise the search's own order.
            var jobs = new JsonArray();

            foreach (var item in scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.Matched))
                jobs.Add(item.Node);

            return new Recommended(query, page, profile.Skills.Count, hasPreferences, detailed, jobs);
        }

        // Whether this person's plan may see how each part scored. Read from the database on every request;
        // if it can't be read they get the smaller view - a plan we couldn't check never earns extra detail.
        private bool ShowsDetail(int userId)
        {
            try
            {
                return PlanLimits.For(_plans.IsPremium(userId)).DetailedScore;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't read the plan of user {UserId}; showing the free view.", userId);

                return false;
            }
        }
    }
}
