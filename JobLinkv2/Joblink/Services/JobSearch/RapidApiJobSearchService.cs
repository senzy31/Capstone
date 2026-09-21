using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Joblink.Services.JobSearch
{
    // Calls the JSearch API (RapidAPI). The key lives in server config ("RapidApi:Key" - dotnet
    // user-secrets or the RapidApi__Key environment variable) and never reaches the browser.
    // "RapidApi:BaseUrl" (optional) points the server at another JSearch-shaped service - the live
    // checks use it to run against a local fake instead of spending the allowance.
    public sealed class RapidApiJobSearchService : IJobSearchService
    {
        private const string DefaultUpstreamBase = "https://jsearch.p.rapidapi.com";
        private const string UpstreamHost = "jsearch.p.rapidapi.com";

        // The free RapidAPI plan allows only 200 requests/month, so
        // successful upstream responses are cached instead of re-fetched.
        private static readonly TimeSpan SearchCacheTime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan DetailCacheTime = TimeSpan.FromHours(24);

        // JSearch latency swings between ~4s and 20s+ on uncached queries.
        private static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(45);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly JobImportService _import;
        private readonly string? _apiKey;
        private readonly string _upstreamBase;

        public RapidApiJobSearchService(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            IConfiguration configuration,
            JobImportService import)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _import = import;
            _apiKey = configuration["RapidApi:Key"];

            var baseUrl = configuration["RapidApi:BaseUrl"];

            _upstreamBase = string.IsNullOrWhiteSpace(baseUrl) ? DefaultUpstreamBase : baseUrl.Trim().TrimEnd('/');
        }

        public async Task<UpstreamResult<JsonElement>> SearchAsync(string query, int page, CancellationToken cancellationToken)
        {
            var taggedKey = $"tagged|{query}|{page}";

            if (_cache.TryGetValue(taggedKey, out JsonElement cachedJobs))
                return UpstreamResult<JsonElement>.Success(cachedJobs);

            var upstream = await CallAsync(
                "/search-v2",
                new Dictionary<string, string>
                {
                    ["query"] = query,
                    ["page"] = page.ToString(),
                    ["num_pages"] = "1"
                },
                SearchCacheTime,
                cancellationToken);

            if (!upstream.Ok)
                return upstream;

            // search-v2 nests the list under data.jobs; flatten it so the
            // frontend always receives data as a plain array.
            if (!TryGetJobs(upstream.Value, out var jobs))
                return UpstreamResult<JsonElement>.Failure(502, "Unexpected response from the job search service.");

            var (tagged, complete) = _import.ImportAndTag(jobs);

            var taggedJobs = JsonSerializer.SerializeToElement(tagged);

            if (complete)
                _cache.Set(taggedKey, taggedJobs, SearchCacheTime);

            return UpstreamResult<JsonElement>.Success(taggedJobs);
        }

        public Task<UpstreamResult<JsonElement>> DetailsAsync(string jobId, CancellationToken cancellationToken) =>
            CallAsync("/job-details", new Dictionary<string, string> { ["job_id"] = jobId }, DetailCacheTime, cancellationToken);

        public Task<UpstreamResult<JsonElement>> SalaryAsync(string jobTitle, string location, CancellationToken cancellationToken) =>
            CallAsync(
                "/estimated-salary",
                new Dictionary<string, string>
                {
                    ["job_title"] = jobTitle,
                    ["location"] = location,
                    ["location_type"] = "ANY",
                    ["years_of_experience"] = "ALL"
                },
                DetailCacheTime,
                cancellationToken);

        private static bool TryGetJobs(JsonElement root, out JsonElement jobs)
        {
            jobs = default;

            if (!root.TryGetProperty("data", out var data))
                return false;

            if (data.ValueKind == JsonValueKind.Array)
            {
                jobs = data;
                return true;
            }

            return data.ValueKind == JsonValueKind.Object &&
                   data.TryGetProperty("jobs", out jobs) &&
                   jobs.ValueKind == JsonValueKind.Array;
        }

        private async Task<UpstreamResult<JsonElement>> CallAsync(
            string path,
            Dictionary<string, string> query,
            TimeSpan cacheFor,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return UpstreamResult<JsonElement>.Failure(503,
                    "The JSearch API key is not configured on the server. " +
                    "Run this inside JobLinkv2/Joblink, then restart the backend: " +
                    "dotnet user-secrets set \"RapidApi:Key\" \"<your RapidAPI key>\"");
            }

            var queryString = string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

            var url = $"{_upstreamBase}{path}?{queryString}";

            if (_cache.TryGetValue(url, out JsonElement cached))
                return UpstreamResult<JsonElement>.Success(cached);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-rapidapi-key", _apiKey);
            request.Headers.Add("x-rapidapi-host", UpstreamHost);

            var client = _httpClientFactory.CreateClient();
            client.Timeout = UpstreamTimeout;

            HttpResponseMessage response;

            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return UpstreamResult<JsonElement>.Failure(504, "The job search service took too long to respond.");
            }
            catch (HttpRequestException)
            {
                return UpstreamResult<JsonElement>.Failure(502, "Could not reach the job search service.");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;

                    if (status == 429)
                        return UpstreamResult<JsonElement>.Failure(429, "The monthly job search limit has been reached. Please try again later.");

                    if (status == 401 || status == 403)
                        return UpstreamResult<JsonElement>.Failure(502, "The job search service rejected the server's API key (invalid, or not subscribed to JSearch).");

                    return UpstreamResult<JsonElement>.Failure(502, $"The job search service returned an error ({status}).");
                }

                JsonElement json;

                try
                {
                    using var document = JsonDocument.Parse(body);
                    json = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    return UpstreamResult<JsonElement>.Failure(502, "Unexpected response from the job search service.");
                }

                _cache.Set(url, json, cacheFor);

                return UpstreamResult<JsonElement>.Success(json);
            }
        }
    }
}
