using System.Net;
using System.Text.Json;
using Joblink.Controllers;
using Joblink.Security;
using Joblink.Tests.Support;
using JobLinkv2.Services;
using Xunit;

namespace Joblink.Tests
{
    // AI resume text spends the server's Anthropic key; job search spends a small monthly
    // RapidAPI allowance. Both were open to anyone. Neither test class can reach the real
    // service: the AI generator is a stand-in that counts, and the search is only checked
    // up to the login (an authenticated search would call RapidAPI).
    public class AiResumeEndpointTests : EndpointTestBase
    {
        public AiResumeEndpointTests(ApiFactory factory) : base(factory) { }

        private const string Summary = "{\"fullName\":\"Maria\",\"skills\":[\"SQL\"]}";
        private const string Description = "{\"position\":\"Dev\",\"companyName\":\"Acme\"}";

        private Task<HttpResponseMessage> AskSummary(HttpClient client, string body = Summary) =>
            Send(client, HttpMethod.Post, "/api/AiResume/summary", body);

        // ----- who may call ------------------------------------------------------

        [Theory]
        [InlineData("/api/AiResume/summary", Summary)]
        [InlineData("/api/AiResume/experience-description", Description)]
        public async Task Both_endpoints_need_a_login_and_cost_nothing_without_one(string url, string body)
        {
            var before = Factory.Ai.Calls;

            var response = await Send(Factory.CreateClient(), HttpMethod.Post, url, body);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(before, Factory.Ai.Calls);
        }

        [Theory]
        [InlineData("/api/AiResume/summary", Summary)]
        [InlineData("/api/AiResume/experience-description", Description)]
        public async Task Employers_have_no_use_for_it(string url, string body)
        {
            var before = Factory.Ai.Calls;

            var response = await Send(Client(NewUser(), "employer"), HttpMethod.Post, url, body);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, Factory.Ai.Calls);
        }

        [Fact]
        public async Task A_job_seeker_gets_their_text()
        {
            var summary = await AskSummary(Client(NewUser()));
            var description = await Send(Client(NewUser()), HttpMethod.Post, "/api/AiResume/experience-description", Description);

            Assert.Equal("A summary for Maria.", (await Read(summary)).GetProperty("summary").GetString());
            Assert.Equal("- Worked as Dev.", (await Read(description)).GetProperty("description").GetString());
        }

        // ----- the hourly limit ---------------------------------------------------

        [Fact]
        public async Task Twenty_requests_an_hour_then_a_429_that_says_when_to_try_again_and_costs_nothing()
        {
            var user = Client(NewUser());

            for (var i = 0; i < AiResumeController.RequestsPerHour; i++)
                Assert.Equal(HttpStatusCode.OK, (await AskSummary(user)).StatusCode);

            var callsBefore = Factory.Ai.Calls;
            var refused = await AskSummary(user);
            var body = await Read(refused);

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
            Assert.Equal(callsBefore, Factory.Ai.Calls);   // the fake was not asked: no tokens were spent
            Assert.Contains("20 AI requests", body.GetProperty("message").GetString());
            Assert.Equal(3600, body.GetProperty("retryAfterSeconds").GetInt32());
            Assert.Equal("3600", refused.Headers.GetValues("Retry-After").Single());
        }

        [Fact]
        public async Task Both_endpoints_share_one_allowance()
        {
            var user = Client(NewUser());

            for (var i = 0; i < 10; i++)
            {
                await AskSummary(user);
                await Send(user, HttpMethod.Post, "/api/AiResume/experience-description", Description);
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, (await AskSummary(user)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(user, HttpMethod.Post, "/api/AiResume/experience-description", Description)).StatusCode);
        }

        [Fact]
        public async Task One_users_limit_does_not_touch_another_user()
        {
            var heavy = Client(NewUser());
            var light = Client(NewUser());

            for (var i = 0; i < 21; i++)
                await AskSummary(heavy);

            Assert.Equal(HttpStatusCode.TooManyRequests, (await AskSummary(heavy)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await AskSummary(light)).StatusCode);
        }

        [Fact]
        public async Task The_hour_rolls_a_slot_frees_as_the_oldest_request_ages_out()
        {
            var user = Client(NewUser());

            for (var i = 0; i < 20; i++)
            {
                Assert.Equal(HttpStatusCode.OK, (await AskSummary(user)).StatusCode);
                Factory.Clock.Advance(TimeSpan.FromMinutes(1));   // spread over 20 minutes
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, (await AskSummary(user)).StatusCode);

            Factory.Clock.Advance(TimeSpan.FromMinutes(40));      // the first one is now an hour old

            Assert.Equal(HttpStatusCode.OK, (await AskSummary(user)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await AskSummary(user)).StatusCode);   // only one slot came back
        }

        [Fact]
        public async Task A_request_that_fails_validation_uses_none_of_the_allowance()
        {
            var user = Client(NewUser());
            var tooLong = $"{{\"notes\":\"{new string('n', 2001)}\"}}";

            for (var i = 0; i < 30; i++)
                Assert.Equal(HttpStatusCode.BadRequest, (await AskSummary(user, tooLong)).StatusCode);

            for (var i = 0; i < 20; i++)
                Assert.Equal(HttpStatusCode.OK, (await AskSummary(user)).StatusCode);
        }

        // ----- what a single request may contain -----------------------------------

        public static IEnumerable<object[]> Oversized() => new[]
        {
            new object[] { "/api/AiResume/summary", "{\"notes\":\"" + new string('n', 2001) + "\"}", "Notes" },
            new object[] { "/api/AiResume/summary", "{\"fullName\":\"" + new string('n', 201) + "\"}", "FullName" },
            new object[] { "/api/AiResume/summary", "{\"skills\":[" + string.Join(",", Enumerable.Repeat("\"x\"", 51)) + "]}", "at most 50" },
            new object[] { "/api/AiResume/summary", "{\"skills\":[\"" + new string('s', 201) + "\"]}", "at most 200" },
            new object[] { "/api/AiResume/summary", "{\"experienceHighlights\":[\"" + new string('e', 1001) + "\"]}", "at most 1000" },
            new object[] { "/api/AiResume/experience-description", "{\"notes\":\"" + new string('n', 2001) + "\"}", "Notes" },
            new object[] { "/api/AiResume/experience-description", "{\"position\":\"" + new string('p', 201) + "\"}", "Position" }
        };

        [Theory]
        [MemberData(nameof(Oversized))]
        public async Task An_oversized_request_is_refused_before_anything_is_paid_for(string url, string body, string mention)
        {
            var before = Factory.Ai.Calls;

            var response = await Send(Client(NewUser()), HttpMethod.Post, url, body);
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(mention, json.GetProperty("message").GetString());
            Assert.Equal(before, Factory.Ai.Calls);
        }

        // ----- failures still say what went wrong ------------------------------------

        [Fact]
        public async Task A_missing_key_is_reported_as_a_503_with_its_message()
        {
            Factory.Ai.Fail = new AiNotConfiguredException("ANTHROPIC_API_KEY is not set.");

            try
            {
                var response = await AskSummary(Client(NewUser()));

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("ANTHROPIC_API_KEY is not set.", (await Read(response)).GetProperty("message").GetString());
            }
            finally
            {
                Factory.Ai.Fail = null;
            }
        }
    }

    public class UserRateLimiterTests
    {
        private readonly TestClock _clock = new();
        private readonly UserRateLimiter _limiter;

        public UserRateLimiterTests() => _limiter = new UserRateLimiter(_clock);

        [Fact]
        public void It_allows_the_limit_then_says_how_long_to_wait()
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.True(_limiter.TryTake("k", 3, TimeSpan.FromHours(1)).Allowed);
                _clock.Advance(TimeSpan.FromMinutes(10));
            }

            var refused = _limiter.TryTake("k", 3, TimeSpan.FromHours(1));

            Assert.False(refused.Allowed);
            Assert.Equal(TimeSpan.FromMinutes(30), refused.RetryAfter);   // the first one is 30 minutes from ageing out
        }

        [Fact]
        public void A_refused_request_is_not_counted()
        {
            _limiter.TryTake("k", 1, TimeSpan.FromHours(1));

            for (var i = 0; i < 10; i++)
                Assert.False(_limiter.TryTake("k", 1, TimeSpan.FromHours(1)).Allowed);

            _clock.Advance(TimeSpan.FromHours(1));

            Assert.True(_limiter.TryTake("k", 1, TimeSpan.FromHours(1)).Allowed);   // only the one real request had to age out
        }

        [Fact]
        public void Keys_are_independent()
        {
            Assert.True(_limiter.TryTake("a", 1, TimeSpan.FromHours(1)).Allowed);
            Assert.False(_limiter.TryTake("a", 1, TimeSpan.FromHours(1)).Allowed);
            Assert.True(_limiter.TryTake("b", 1, TimeSpan.FromHours(1)).Allowed);
        }

        [Fact]
        public void Parallel_requests_never_get_more_than_the_limit()
        {
            var allowed = 0;

            Parallel.For(0, 200, _ =>
            {
                if (_limiter.TryTake("k", 20, TimeSpan.FromHours(1)).Allowed)
                    Interlocked.Increment(ref allowed);
            });

            Assert.Equal(20, allowed);
        }
    }

    public class JobSearchAccessTests : EndpointTestBase
    {
        public JobSearchAccessTests(ApiFactory factory) : base(factory) { }

        [Theory]
        [InlineData("/api/JobSearch/search?query=developer&page=1")]
        [InlineData("/api/JobSearch/details?jobId=abc")]
        [InlineData("/api/JobSearch/salary?jobTitle=developer&location=Manila")]
        public async Task Every_search_endpoint_needs_a_login(string url)
        {
            var response = await Factory.CreateClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("expired")]
        [InlineData("wrong-key")]
        public async Task A_bad_token_is_no_better_than_none(string kind)
        {
            var user = NewUser();
            var token = kind == "expired"
                ? ApiFactory.MakeToken(user, lifetime: TimeSpan.FromMinutes(-10))
                : ApiFactory.MakeToken(user, key: "some-other-signing-key-that-is-also-long-enough-123");

            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/JobSearch/search?query=developer&page=1")).StatusCode);
        }
    }
}
