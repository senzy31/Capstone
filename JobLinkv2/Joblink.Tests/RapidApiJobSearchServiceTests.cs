using System.Net;
using System.Text;
using System.Text.Json;
using Joblink.Services;
using Joblink.Services.JobSearch;
using Joblink.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Joblink.Tests
{
    // The JSearch client: what it asks for, what it caches, how each way of failing is reported, and that the key
    // and address never leak into what a browser sees. No network - the far end is a stub.
    public class RapidApiJobSearchServiceTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new();

            public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, "{}");

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);

                return Task.FromResult(Respond(request));
            }
        }

        private sealed class StubFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public StubFactory(HttpMessageHandler handler) => _handler = handler;

            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private readonly StubHandler _handler = new();
        private readonly InMemoryApplyStore _store = new();

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static string SearchBody(params string[] titles) =>
            JsonSerializer.Serialize(new
            {
                status = "OK",
                data = new
                {
                    jobs = titles.Select((title, i) => new
                    {
                        job_id = $"upstream-{title}-{i}",
                        job_title = title,
                        employer_name = "Acme",
                        job_apply_link = $"https://www.linkedin.com/jobs/view/{i}",
                        job_publisher = "LinkedIn"
                    })
                }
            });

        private RapidApiJobSearchService Service(string? key = "test-key", string? baseUrl = null)
        {
            var settings = new Dictionary<string, string?> { ["RapidApi:Key"] = key, ["RapidApi:BaseUrl"] = baseUrl };

            return new RapidApiJobSearchService(
                new StubFactory(_handler),
                new MemoryCache(new MemoryCacheOptions()),
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                new JobImportService(_store, NullLogger<JobImportService>.Instance));
        }

        private static string[] Titles(JsonElement jobs) => jobs.EnumerateArray().Select(j => j.GetProperty("job_title").GetString()!).ToArray();

        // ----- what it asks for -------------------------------------------------------------------------------------

        [Fact]
        public async Task A_search_asks_JSearch_for_one_page_with_the_key_and_host_headers()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, SearchBody("Chef"));

            await Service().SearchAsync("chef jobs in Cebu", 2, default);

            var request = Assert.Single(_handler.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://jsearch.p.rapidapi.com/search-v2?query=chef%20jobs%20in%20Cebu&page=2&num_pages=1", request.RequestUri!.OriginalString);
            Assert.Equal("test-key", request.Headers.GetValues("x-rapidapi-key").Single());
            Assert.Equal("jsearch.p.rapidapi.com", request.Headers.GetValues("x-rapidapi-host").Single());
        }

        [Theory]
        [InlineData("http://127.0.0.1:5000", "http://127.0.0.1:5000/search-v2")]
        [InlineData("http://127.0.0.1:5000/", "http://127.0.0.1:5000/search-v2")]
        [InlineData("  http://fake.example/api/  ", "http://fake.example/api/search-v2")]
        [InlineData("", "https://jsearch.p.rapidapi.com/search-v2")]
        [InlineData("   ", "https://jsearch.p.rapidapi.com/search-v2")]
        public async Task The_service_address_can_be_pointed_elsewhere_by_configuration(string baseUrl, string expectedStart)
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, SearchBody("Chef"));

            await Service(baseUrl: baseUrl).SearchAsync("chef", 1, default);

            Assert.StartsWith(expectedStart + "?", _handler.Requests.Single().RequestUri!.ToString());
        }

        [Fact]
        public async Task A_query_is_escaped_so_it_cannot_add_parameters()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, SearchBody("Chef"));

            await Service().SearchAsync("a&page=9&num_pages=5 café", 1, default);

            var query = System.Web.HttpUtility.ParseQueryString(_handler.Requests.Single().RequestUri!.Query);

            Assert.Equal("a&page=9&num_pages=5 café", query["query"]);
            Assert.Equal("1", query["page"]);
            Assert.Equal("1", query["num_pages"]);
        }

        [Fact]
        public async Task Details_and_salary_ask_for_their_own_endpoints()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, "{\"status\":\"OK\",\"data\":[]}");
            var service = Service();

            await service.DetailsAsync("job/1?x", default);
            await service.SalaryAsync("QA Engineer", "Cebu, Philippines", default);

            var details = _handler.Requests[0].RequestUri!;
            var salary = _handler.Requests[1].RequestUri!;

            Assert.Equal("/job-details", details.AbsolutePath);
            Assert.Equal("job/1?x", System.Web.HttpUtility.ParseQueryString(details.Query)["job_id"]);
            Assert.Equal("/estimated-salary", salary.AbsolutePath);
            var salaryQuery = System.Web.HttpUtility.ParseQueryString(salary.Query);
            Assert.Equal(new[] { "QA Engineer", "Cebu, Philippines", "ANY", "ALL" }, new[] { salaryQuery["job_title"], salaryQuery["location"], salaryQuery["location_type"], salaryQuery["years_of_experience"] });
        }

        // ----- what it returns -----------------------------------------------------------------------------------------

        [Fact]
        public async Task The_jobs_are_flattened_saved_and_tagged()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, SearchBody("Chef", "Cook"));

            var result = await Service().SearchAsync("chef", 1, default);

            Assert.True(result.Ok);
            Assert.Equal(new[] { "Chef", "Cook" }, Titles(result.Value));

            var ids = result.Value.EnumerateArray().Select(j => j.GetProperty("joblink_job_id").GetInt32()).ToArray();

            Assert.Equal(2, ids.Distinct().Count());
            Assert.All(result.Value.EnumerateArray(), job => Assert.Equal("External", job.GetProperty("joblink_source").GetString()));
            Assert.Equal(2, _store.Listings.Count);
            Assert.Equal("upstream-Chef-0", job0(result).GetProperty("job_id").GetString());      // the original fields are still there

            static JsonElement job0(UpstreamResult<JsonElement> r) => r.Value.EnumerateArray().First();
        }

        [Fact]
        public async Task A_plain_array_under_data_is_accepted_too()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, "{\"status\":\"OK\",\"data\":[{\"job_id\":\"x\",\"job_title\":\"Chef\"}]}");

            var result = await Service().SearchAsync("chef", 1, default);

            Assert.Equal(new[] { "Chef" }, Titles(result.Value));
        }

        // ----- the cache ----------------------------------------------------------------------------------------------

        [Fact]
        public async Task The_same_search_is_answered_from_the_cache_without_asking_JSearch_again()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, SearchBody("Chef"));
            var service = Service();

            var first = await service.SearchAsync("chef", 1, default);
            var second = await service.SearchAsync("chef", 1, default);

            Assert.Single(_handler.Requests);
            Assert.Equal(first.Value.GetRawText(), second.Value.GetRawText());
        }

        [Fact]
        public async Task A_different_query_or_page_is_a_different_search()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, SearchBody("Chef"));
            var service = Service();

            await service.SearchAsync("chef", 1, default);
            await service.SearchAsync("chef", 2, default);
            await service.SearchAsync("cook", 1, default);

            Assert.Equal(3, _handler.Requests.Count);
        }

        [Fact]
        public async Task A_failure_is_not_cached_so_the_next_try_asks_again()
        {
            var failing = true;
            _handler.Respond = _ => failing ? Json(HttpStatusCode.InternalServerError, "oops") : Json(HttpStatusCode.OK, SearchBody("Chef"));
            var service = Service();

            Assert.False((await service.SearchAsync("chef", 1, default)).Ok);

            failing = false;

            Assert.True((await service.SearchAsync("chef", 1, default)).Ok);
            Assert.Equal(2, _handler.Requests.Count);
        }

        [Fact]
        public async Task Jobs_that_could_not_be_saved_are_returned_untagged_and_saved_on_the_next_ask_without_another_call()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, SearchBody("Chef"));
            _store.ImportsFail = true;
            var service = Service();

            var result = await service.SearchAsync("chef", 1, default);

            Assert.True(result.Ok);                                                        // the search still works
            Assert.False(result.Value.EnumerateArray().First().TryGetProperty("joblink_job_id", out _));

            _store.ImportsFail = false;
            var again = await service.SearchAsync("chef", 1, default);

            Assert.Single(_handler.Requests);                                               // JSearch's own answer was kept: no second call spent...
            Assert.True(again.Value.EnumerateArray().First().TryGetProperty("joblink_job_id", out _));   // ...and this time the jobs were saved and tagged
        }

        [Fact]
        public async Task Details_are_cached_for_the_next_asker()
        {
            _handler.Respond = _ => Json(HttpStatusCode.OK, "{\"status\":\"OK\",\"data\":[{\"job_description\":\"FULL\"}]}");
            var service = Service();

            await service.DetailsAsync("abc", default);
            await service.DetailsAsync("abc", default);

            Assert.Single(_handler.Requests);
        }

        // ----- each way of failing ------------------------------------------------------------------------------------

        [Theory]
        [InlineData(429, 429, "The monthly job search limit has been reached. Please try again later.")]
        [InlineData(401, 502, "The job search service rejected the server's API key (invalid, or not subscribed to JSearch).")]
        [InlineData(403, 502, "The job search service rejected the server's API key (invalid, or not subscribed to JSearch).")]
        [InlineData(500, 502, "The job search service returned an error (500).")]
        [InlineData(404, 502, "The job search service returned an error (404).")]
        public async Task An_upstream_error_becomes_our_own_status_and_message(int upstream, int status, string message)
        {
            _handler.Respond = _ => Json((HttpStatusCode)upstream, "{\"message\":\"secret upstream detail\"}");

            var result = await Service().SearchAsync("chef", 1, default);

            Assert.False(result.Ok);
            Assert.Equal(status, result.Status);
            Assert.Equal(message, result.Message);
            Assert.DoesNotContain("secret upstream detail", result.Message);
        }

        [Fact]
        public async Task An_answer_that_is_not_json_or_not_a_job_list_is_a_bad_gateway()
        {
            var service = Service();

            _handler.Respond = _ => Json(HttpStatusCode.OK, "<html>not json</html>");
            var notJson = await service.SearchAsync("a", 1, default);

            _handler.Respond = _ => Json(HttpStatusCode.OK, "{\"status\":\"OK\"}");
            var noData = await service.SearchAsync("b", 1, default);

            _handler.Respond = _ => Json(HttpStatusCode.OK, "{\"data\":{\"jobs\":\"none\"}}");
            var notAList = await service.SearchAsync("c", 1, default);

            Assert.All(new[] { notJson, noData, notAList }, r => Assert.Equal(502, r.Status));
            Assert.Equal("Unexpected response from the job search service.", notJson.Message);
            Assert.Equal("Unexpected response from the job search service.", noData.Message);
            Assert.Equal("Unexpected response from the job search service.", notAList.Message);
        }

        [Fact]
        public async Task A_service_that_cannot_be_reached_is_a_bad_gateway()
        {
            _handler.Respond = _ => throw new HttpRequestException("connection refused to 10.0.0.5");

            var result = await Service().SearchAsync("chef", 1, default);

            Assert.Equal(502, result.Status);
            Assert.Equal("Could not reach the job search service.", result.Message);
            Assert.DoesNotContain("10.0.0.5", result.Message);
        }

        [Fact]
        public async Task A_service_that_takes_too_long_is_a_gateway_timeout()
        {
            _handler.Respond = _ => throw new TaskCanceledException("timed out", new TimeoutException());

            var result = await Service().SearchAsync("chef", 1, default);

            Assert.Equal(504, result.Status);
            Assert.Equal("The job search service took too long to respond.", result.Message);
        }

        [Fact]
        public async Task A_caller_that_gave_up_is_not_told_the_service_timed_out()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            _handler.Respond = _ => throw new TaskCanceledException();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().SearchAsync("chef", 1, cancelled.Token));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Without_a_key_nothing_is_asked_and_the_answer_says_how_to_set_one(string? key)
        {
            var result = await Service(key: key).SearchAsync("chef", 1, default);

            Assert.Equal(503, result.Status);
            Assert.Contains("dotnet user-secrets set", result.Message);
            Assert.Empty(_handler.Requests);
        }

        [Fact]
        public async Task The_key_is_never_part_of_what_is_returned()
        {
            _handler.Respond = _ => Json(HttpStatusCode.Unauthorized, "bad key test-key");

            var result = await Service(key: "test-key").SearchAsync("chef", 1, default);

            Assert.DoesNotContain("test-key", result.Message);
        }
    }
}
