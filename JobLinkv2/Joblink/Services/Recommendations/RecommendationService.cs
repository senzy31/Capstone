using System.Text.Json;
using System.Text.Json.Nodes;
using Joblink.Services.JobSearch;
using JobLinkv2.Models;
using JobLinkv2.Services.Apply;
using JobLinkv2.Services.Employer;
using JobLinkv2.Services.Matching;
using JobLinkv2.Services.Subscriptions;

namespace Joblink.Services.Recommendations
{
    public abstract record RecommendationOutcome;

    // The jobs, best match first, each with its match (joblink_match) already cut down to what the plan may see.
    public sealed record Recommended(string? Query, int Page, int SkillCount, bool HasPreferences, bool Detailed, JsonArray Jobs) : RecommendationOutcome;

    // The job search failed; Status and Message are fit to hand to the client as they are.
    public sealed record RecommendationFailed(int Status, string Message) : RecommendationOutcome;

    // What GET /api/Recommendations/search takes beyond the query text - the same filters the
    // Jobs page already offers, now applied on the server so they cover internal jobs too.
    public sealed record SearchFilters(
        string? WorkArrangement = null, string? Location = null,
        double? MinSalary = null, double? MaxSalary = null, string? JobType = null, int? MinScore = null);

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
        private readonly EmployerJobStore _internalJobs;
        private readonly TimeProvider _time;
        private readonly ILogger<RecommendationService> _logger;

        public RecommendationService(
            IScoringProfileReader profiles, IJobSearchService search, IPlanReader plans,
            EmployerJobStore internalJobs, TimeProvider time, ILogger<RecommendationService> logger)
        {
            _profiles = profiles;
            _search = search;
            _plans = plans;
            _internalJobs = internalJobs;
            _time = time;
            _logger = logger;
        }

        public async Task<RecommendationOutcome> GetAsync(int userId, int page, CancellationToken cancellationToken)
        {
            var profile = _profiles.Read(userId);

            var hasPreferences = profile.Preferences is { } p &&
                (!string.IsNullOrEmpty(p.PreferredLocation) || !string.IsNullOrEmpty(p.WorkArrangement) || p.MinSalary != 0 || p.MaxSalary != 0);

            var detailed = ShowsDetail(userId);

            var internalScored = ScoreInternalJobs(profile, detailed, null);

            // Nothing to match on: still show internal jobs (they cost no search quota), but
            // there's nothing to build a JSearch query from.
            if (profile.Skills.Count == 0)
                return new Recommended(null, page, 0, hasPreferences, detailed, Merge(internalScored, Enumerable.Empty<ScoredJob>()));

            var query = RecommendationQuery.Build(profile);

            var found = await _search.SearchAsync(query, page, cancellationToken);

            if (!found.Ok)
                return new RecommendationFailed(found.Status, found.Message!);

            var externalScored = ScoreExternalJobs(found.Value, profile, detailed, null);

            return new Recommended(query, page, profile.Skills.Count, hasPreferences, detailed, Merge(internalScored, externalScored));
        }

        // GET /api/Recommendations/search: a job seeker's own search text and filters, instead of
        // their resume. Internal jobs matching the same text/filters are scored the same way and
        // placed first (best match, then newest); external jobs go through the same JSearch call
        // (and cache) JobSearchController already uses, so this spends no extra RapidAPI quota.
        public async Task<RecommendationOutcome> SearchAsync(int userId, string? query, int page, SearchFilters filters, CancellationToken cancellationToken)
        {
            var profile = _profiles.Read(userId);
            var detailed = ShowsDetail(userId);

            var hasPreferences = profile.Preferences is { } p &&
                (!string.IsNullOrEmpty(p.PreferredLocation) || !string.IsNullOrEmpty(p.WorkArrangement) || p.MinSalary != 0 || p.MaxSalary != 0);

            var trimmedQuery = (query ?? "").Trim();
            var effectiveQuery = trimmedQuery.Length > 0 ? trimmedQuery : RecommendationQuery.Build(profile);

            if (effectiveQuery.Length == 0)
                return new Recommended(null, page, profile.Skills.Count, hasPreferences, detailed, Merge(ScoreInternalJobs(profile, detailed, filters, trimmedQuery), Enumerable.Empty<ScoredJob>()));

            var found = await _search.SearchAsync(effectiveQuery, page, cancellationToken);

            if (!found.Ok)
                return new RecommendationFailed(found.Status, found.Message!);

            var internalScored = ScoreInternalJobs(profile, detailed, filters, trimmedQuery);
            var externalScored = ScoreExternalJobs(found.Value, profile, detailed, filters);

            return new Recommended(effectiveQuery, page, profile.Skills.Count, hasPreferences, detailed, Merge(internalScored, externalScored));
        }

        // ------------------------------------------------------------------
        // Scoring and merging
        // ------------------------------------------------------------------

        // Node plus what it's ordered by. PublishedAt only matters for internal jobs (their own
        // "then newest" tiebreak); it's always null for external ones, where the search's own
        // order is the last word.
        private readonly record struct ScoredJob(JsonObject Node, int Score, int Matched, DateTime? PublishedAt);

        // Internal jobs (JobLink's own paying employers) come first as a whole block - best match,
        // then newest - regardless of how any external job scored; external jobs follow, in their
        // own best-match order. Never merged and re-sorted together as one list.
        private static JsonArray Merge(IEnumerable<ScoredJob> internalJobs, IEnumerable<ScoredJob> externalJobs)
        {
            var jobs = new JsonArray();

            foreach (var item in internalJobs.OrderByDescending(s => s.Score).ThenByDescending(s => s.Matched).ThenByDescending(s => s.PublishedAt))
                jobs.Add(item.Node);

            foreach (var item in externalJobs.OrderByDescending(s => s.Score).ThenByDescending(s => s.Matched))
                jobs.Add(item.Node);

            return jobs;
        }

        private IEnumerable<ScoredJob> ScoreExternalJobs(
            JsonElement found, ScoringProfile profile, bool detailed, SearchFilters? filters)
        {
            foreach (var element in found.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                var job = ScoringJob.FromJson(element);

                if (filters != null && !PassesCommonFilters(job, filters))
                    continue;

                if (filters?.JobType is { Length: > 0 } jobType && !ExternalJobTypeMatches(element, jobType))
                    continue;

                var result = SuitabilityScorer.Score(job, profile);

                if (filters?.MinScore is { } minScore && result.Score < minScore)
                    continue;

                var match = MatchPresenter.For(result, job, profile, detailed);

                // A copy: what the search returns is cached and shared, and must not be changed.
                var node = JsonNode.Parse(element.GetRawText())!.AsObject();
                node["joblink_match"] = JsonSerializer.SerializeToNode(match, match.GetType(), Web);
                node["joblink_source"] ??= ListingSources.External;

                yield return new ScoredJob(node, result.Score, result.Skills?.Matched.Count ?? 0, null);
            }
        }

        private IEnumerable<ScoredJob> ScoreInternalJobs(
            ScoringProfile profile, bool detailed, SearchFilters? filters, string? searchText = null)
        {
            // Internal jobs are an addition to recommendations, never a requirement for them - if
            // the table can't be read, external results must still come back (same spirit as
            // ApplyService.IsPremium: a plan/table that can't be read never breaks the core action).
            IReadOnlyList<JoblistingModel> listings;

            try
            {
                listings = _internalJobs.ListActiveInternalJobs(_time.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't read internal job listings; showing external results only.");
                yield break;
            }

            foreach (var listing in listings)
            {
                if (!string.IsNullOrEmpty(searchText) && !MatchesText(listing, searchText))
                    continue;

                var job = ToScoringJob(listing);

                if (filters != null && !PassesCommonFilters(job, filters))
                    continue;

                if (filters?.JobType is { Length: > 0 } jobType && !string.Equals(listing.JobType, jobType, StringComparison.OrdinalIgnoreCase))
                    continue;

                var result = SuitabilityScorer.Score(job, profile);

                if (filters?.MinScore is { } minScore && result.Score < minScore)
                    continue;

                var match = MatchPresenter.For(result, job, profile, detailed);

                yield return new ScoredJob(ToNode(listing, match), result.Score, result.Skills?.Matched.Count ?? 0, listing.PublishedAt);
            }
        }

        private static bool MatchesText(JoblistingModel job, string searchText)
        {
            var haystack = $"{job.Title} {job.Company} {job.Description}".ToLowerInvariant();

            return haystack.Contains(searchText.ToLowerInvariant(), StringComparison.Ordinal);
        }

        // Work arrangement and location: the two filters that apply the same way to every job,
        // internal or external. Salary and job type are checked by each caller - the raw fields
        // they come from (job_employment_type, job_min_salary...) don't live on ScoringJob.
        private static bool PassesCommonFilters(ScoringJob job, SearchFilters filters)
        {
            if (!string.IsNullOrWhiteSpace(filters.WorkArrangement))
            {
                if (!string.Equals(SuitabilityScorer.WorkSetup(job), filters.WorkArrangement, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(filters.Location))
            {
                var place = $"{job.City} {job.State} {job.Country} {job.Location}".ToLowerInvariant();

                if (!place.Contains(filters.Location.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    return false;
            }

            // A job with no listed salary always passes - there's nothing to compare.
            if (job.MinSalary != 0 || job.MaxSalary != 0)
            {
                var jobMin = job.MinSalary;
                var jobMax = job.MaxSalary != 0 ? job.MaxSalary : double.PositiveInfinity;

                if (filters.MinSalary is { } wantMin && wantMin > 0 && jobMax < wantMin)
                    return false;

                if (filters.MaxSalary is { } wantMax && wantMax > 0 && jobMin > wantMax)
                    return false;
            }

            return true;
        }

        // JSearch's job_employment_type is a display label ("Full-time"); the machine-readable
        // codes (FULLTIME, PARTTIME, ...) live in job_employment_types. Same rule DASHBOARD/Jobs.js
        // used client-side.
        private static bool ExternalJobTypeMatches(JsonElement element, string jobType)
        {
            var types = new List<string>();

            if (element.TryGetProperty("job_employment_types", out var many) && many.ValueKind == JsonValueKind.Array)
                types.AddRange(many.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString() ?? ""));

            if (types.Count == 0 && element.TryGetProperty("job_employment_type", out var one) && one.ValueKind == JsonValueKind.String)
                types.Add(one.GetString() ?? "");

            return types.Any(t => string.Equals(NormalizeJobType(t), jobType, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeJobType(string type) =>
            new string(type.Where(char.IsAsciiLetter).ToArray()).ToUpperInvariant();

        // An internal job has no such column: the scorer infers work setup from IsRemote and the
        // description text (there's no field for it in JSearch either), but an internal job
        // already has an authoritative WorkSetup - folded into the scoring-only text below so the
        // inference agrees with it, never into what's actually shown to anyone.
        private static ScoringJob ToScoringJob(JoblistingModel job)
        {
            var description = job.Description ?? "";

            if (string.Equals(job.WorkSetup, "hybrid", StringComparison.OrdinalIgnoreCase) &&
                !description.Contains("hybrid", StringComparison.OrdinalIgnoreCase))
                description += " (Hybrid)";

            return new ScoringJob(
                Title: job.Title,
                Description: description,
                Location: job.Location,
                IsRemote: string.Equals(job.WorkSetup, "remote", StringComparison.OrdinalIgnoreCase),
                // Employer-entered salaries are monthly PHP, same unit ScoringPreferences uses -
                // there's no currency/period picker in the posting form.
                MinSalary: (double)(job.SalaryMin ?? 0),
                MaxSalary: (double)(job.SalaryMax ?? 0),
                SalaryPeriod: "MONTH",
                SalaryCurrency: "PHP");
        }

        // A JSearch-shaped node so the existing job card rendering needs only to add a "Posted on
        // JobLink" badge and its own Apply button, not a second rendering path. joblink_job_id and
        // joblink_source match exactly what JobImportService tags external jobs with.
        private static JsonObject ToNode(JoblistingModel job, object match)
        {
            var node = new JsonObject
            {
                ["joblink_job_id"] = job.JobId,
                ["joblink_source"] = ListingSources.Internal,
                ["job_title"] = job.Title,
                ["job_description"] = job.Description,
                ["job_city"] = job.Location,
                ["employer_name"] = job.Company,
                ["job_is_remote"] = string.Equals(job.WorkSetup, "remote", StringComparison.OrdinalIgnoreCase),
                ["job_employment_type"] = job.JobType,
                ["job_employment_types"] = job.JobType is { Length: > 0 } jt ? new JsonArray(jt) : new JsonArray(),
                ["job_min_salary"] = job.SalaryMin,
                ["job_max_salary"] = job.SalaryMax,
                ["job_salary_currency"] = "PHP",
                ["job_salary_period"] = "MONTH",
                ["job_posted_at_datetime_utc"] = job.PublishedAt,
                ["joblink_work_setup"] = job.WorkSetup,
                ["joblink_expires_at"] = job.ExpiresAt
            };

            node["joblink_match"] = JsonSerializer.SerializeToNode(match, match.GetType(), Web);

            return node;
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
