using System.Net;
using System.Text.Json;
using Xunit;

namespace Joblink.Tests
{
    // The job search endpoints once the JSearch client moved out of the controller: what is checked before a
    // search is spent, and how an answer or a failure is shaped. (Who may call is in JobSearchAccessTests, what is
    // asked of JSearch and cached in RapidApiJobSearchServiceTests.)
    public class JobSearchEndpointTests : EndpointTestBase
    {
        public JobSearchEndpointTests(Support.ApiFactory factory) : base(factory) { }

        [Fact]
        public async Task A_search_returns_status_and_the_jobs()
        {
            Factory.Search.Returns(new { job_id = "a", job_title = "Chef", joblink_job_id = 7 });

            var response = await Client(NewUser()).GetAsync("/api/JobSearch/search?query=chef");
            var body = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("OK", body.GetProperty("status").GetString());
            Assert.Equal("Chef", body.GetProperty("data")[0].GetProperty("job_title").GetString());
        }

        [Fact]
        public async Task An_employer_may_search_too()
        {
            Factory.Search.Returns();

            Assert.Equal(HttpStatusCode.OK, (await Client(NewUser(), role: "employer").GetAsync("/api/JobSearch/search?query=chef")).StatusCode);
        }

        [Theory]
        [InlineData("", "query is required")]
        [InlineData("   ", "query is required")]
        public async Task A_blank_query_is_refused_before_a_search_is_spent(string query, string message)
        {
            Factory.Search.Returns();
            var before = Factory.Search.Searches.Count;

            var response = await Client(NewUser()).GetAsync($"/api/JobSearch/search?query={query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, (await Read(response)).GetProperty("message").GetString());
            Assert.Equal(before, Factory.Search.Searches.Count);
        }

        [Fact]
        public async Task A_query_over_200_characters_is_refused_and_200_is_fine()
        {
            Factory.Search.Returns();

            var tooLong = await Client(NewUser()).GetAsync("/api/JobSearch/search?query=" + new string('a', 201));
            var longest = await Client(NewUser()).GetAsync("/api/JobSearch/search?query=" + new string('a', 200));

            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Equal("query is too long", (await Read(tooLong)).GetProperty("message").GetString());
            Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
        }

        [Fact]
        public async Task The_query_is_trimmed_and_the_page_kept_between_1_and_10()
        {
            Factory.Search.Returns();
            var client = Client(NewUser());

            await client.GetAsync("/api/JobSearch/search?query=%20%20chef%20jobs%20%20&page=0");
            Assert.Equal(("chef jobs", 1), Factory.Search.Searches[^1]);

            await client.GetAsync("/api/JobSearch/search?query=chef&page=50");
            Assert.Equal(("chef", 10), Factory.Search.Searches[^1]);

            await client.GetAsync("/api/JobSearch/search?query=chef");
            Assert.Equal(("chef", 1), Factory.Search.Searches[^1]);
        }

        [Theory]
        [InlineData(429, "The monthly job search limit has been reached. Please try again later.")]
        [InlineData(502, "Unexpected response from the job search service.")]
        [InlineData(503, "The JSearch API key is not configured on the server.")]
        [InlineData(504, "The job search service took too long to respond.")]
        public async Task A_failed_search_is_reported_with_its_status_and_a_message_and_no_data(int status, string message)
        {
            Factory.Search.Fails(status, message);

            var response = await Client(NewUser()).GetAsync("/api/JobSearch/search?query=chef");
            var body = await Read(response);

            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(message, body.GetProperty("message").GetString());
            Assert.False(body.TryGetProperty("data", out _));
        }

        [Theory]
        [InlineData("/api/JobSearch/details", "a valid jobId is required")]
        [InlineData("/api/JobSearch/details?jobId=%20", "a valid jobId is required")]
        [InlineData("/api/JobSearch/salary?location=Manila", "jobTitle and location are required")]
        [InlineData("/api/JobSearch/salary?jobTitle=Chef", "jobTitle and location are required")]
        public async Task Missing_details_and_salary_input_is_refused(string url, string message)
        {
            var response = await Client(NewUser()).GetAsync(url);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, (await Read(response)).GetProperty("message").GetString());
        }

        [Fact]
        public async Task Over_long_details_and_salary_input_is_refused()
        {
            var client = Client(NewUser());

            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/JobSearch/details?jobId=" + new string('x', 1001))).StatusCode);

            var longTitle = await client.GetAsync("/api/JobSearch/salary?jobTitle=" + new string('x', 201) + "&location=Manila");
            var longLocation = await client.GetAsync("/api/JobSearch/salary?jobTitle=Chef&location=" + new string('x', 201));
            var longest = await client.GetAsync("/api/JobSearch/salary?jobTitle=" + new string('x', 200) + "&location=" + new string('y', 200));

            Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, longLocation.StatusCode);
            Assert.Equal("jobTitle or location is too long", (await Read(longLocation)).GetProperty("message").GetString());
            Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
        }

        [Fact]
        public async Task Details_and_salary_return_the_service_answer_as_it_is()
        {
            var client = Client(NewUser());

            var details = await Read(await client.GetAsync("/api/JobSearch/details?jobId=abc"));
            var salary = await Read(await client.GetAsync("/api/JobSearch/salary?jobTitle=Chef&location=Manila"));

            Assert.Equal("OK", details.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Array, salary.GetProperty("data").ValueKind);
        }
    }
}
