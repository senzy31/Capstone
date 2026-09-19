using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace Joblink.Controllers
{
    // Server-side proxy for the JSearch job feed (RapidAPI). The RapidAPI key
    // lives in server config ("RapidApi:Key" - dotnet user-secrets or the
    // RapidApi__Key environment variable) and never reaches the browser.
    [Route("api/[controller]")]
    [ApiController]
    public class JobSearchController : ControllerBase
    {
        private const string UpstreamBase = "https://jsearch.p.rapidapi.com";
        private const string UpstreamHost = "jsearch.p.rapidapi.com";

        // The free RapidAPI plan allows only 200 requests/month, so
        // successful upstream responses are cached instead of re-fetched.
        private static readonly TimeSpan SearchCacheTime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan DetailCacheTime = TimeSpan.FromHours(24);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly string? _apiKey;

        public JobSearchController(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _apiKey = configuration["RapidApi:Key"];
        }

        // GET api/JobSearch/search?query=developer%20manila&page=1
        // Returns { status, data: [ ...jobs ] }
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] int page = 1)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "query is required" });

            if (query.Length > 200)
                return BadRequest(new { message = "query is too long" });

            var (json, error) = await CallJSearch(
                "/search-v2",
                new Dictionary<string, string>
                {
                    ["query"] = query.Trim(),
                    ["page"] = Math.Clamp(page, 1, 10).ToString(),
                    ["num_pages"] = "1"
                },
                SearchCacheTime);

            if (error != null)
                return error;

            // search-v2 nests the list under data.jobs; flatten it so the
            // frontend always receives data as a plain array.
            var root = json!.Value;

            if (root.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.Array)
                    return Ok(new { status = "OK", data });

                if (data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("jobs", out var jobs) &&
                    jobs.ValueKind == JsonValueKind.Array)
                    return Ok(new { status = "OK", data = jobs });
            }

            return StatusCode(502, new { message = "Unexpected response from the job search service." });
        }

        // GET api/JobSearch/details?jobId=...
        [HttpGet("details")]
        public async Task<IActionResult> Details([FromQuery] string? jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 1000)
                return BadRequest(new { message = "a valid jobId is required" });

            var (json, error) = await CallJSearch(
                "/job-details",
                new Dictionary<string, string> { ["job_id"] = jobId },
                DetailCacheTime);

            return error ?? Ok(json);
        }

        // GET api/JobSearch/salary?jobTitle=...&location=...
        [HttpGet("salary")]
        public async Task<IActionResult> Salary([FromQuery] string? jobTitle, [FromQuery] string? location)
        {
            if (string.IsNullOrWhiteSpace(jobTitle) || string.IsNullOrWhiteSpace(location))
                return BadRequest(new { message = "jobTitle and location are required" });

            if (jobTitle.Length > 200 || location.Length > 200)
                return BadRequest(new { message = "jobTitle or location is too long" });

            var (json, error) = await CallJSearch(
                "/estimated-salary",
                new Dictionary<string, string>
                {
                    ["job_title"] = jobTitle.Trim(),
                    ["location"] = location.Trim(),
                    ["location_type"] = "ANY",
                    ["years_of_experience"] = "ALL"
                },
                DetailCacheTime);

            return error ?? Ok(json);
        }

        private async Task<(JsonElement? Json, IActionResult? Error)> CallJSearch(
            string path,
            Dictionary<string, string> query,
            TimeSpan cacheFor)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return (null, StatusCode(503, new
                {
                    message = "The JSearch API key is not configured on the server. " +
                              "Run this inside JobLinkv2/Joblink, then restart the backend: " +
                              "dotnet user-secrets set \"RapidApi:Key\" \"<your RapidAPI key>\""
                }));
            }

            var queryString = string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

            var url = $"{UpstreamBase}{path}?{queryString}";

            if (_cache.TryGetValue(url, out JsonElement cached))
                return (cached, null);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-rapidapi-key", _apiKey);
            request.Headers.Add("x-rapidapi-host", UpstreamHost);

            // JSearch latency swings between ~4s and 20s+ on uncached queries.
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(45);

            HttpResponseMessage response;

            try
            {
                response = await client.SendAsync(request, HttpContext.RequestAborted);
            }
            catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                return (null, StatusCode(504, new { message = "The job search service took too long to respond." }));
            }
            catch (HttpRequestException)
            {
                return (null, StatusCode(502, new { message = "Could not reach the job search service." }));
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;

                    if (status == 429)
                        return (null, StatusCode(429, new { message = "The monthly job search limit has been reached. Please try again later." }));

                    if (status == 401 || status == 403)
                        return (null, StatusCode(502, new { message = "The job search service rejected the server's API key (invalid, or not subscribed to JSearch)." }));

                    return (null, StatusCode(502, new { message = $"The job search service returned an error ({status})." }));
                }

                JsonElement json;

                try
                {
                    using var document = JsonDocument.Parse(body);
                    json = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    return (null, StatusCode(502, new { message = "Unexpected response from the job search service." }));
                }

                _cache.Set(url, json, cacheFor);

                return (json, null);
            }
        }
    }
}
