using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobLinkv2.Models;
using Joblink.Tests.Support;
using Xunit;

namespace Joblink.Tests
{
    public abstract class EndpointTestBase : IClassFixture<ApiFactory>
    {
        private static int _nextUser = 1000;

        protected ApiFactory Factory { get; }
        protected InMemoryApplyStore Store => Factory.Store;

        protected EndpointTestBase(ApiFactory factory) => Factory = factory;

        // Every test uses its own user, so tests can share one in-memory app.
        protected static int NewUser() => Interlocked.Increment(ref _nextUser);

        protected HttpClient Client(int userId, string role = "user") => Factory.ClientFor(userId, role);

        protected static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

        protected static async Task<JsonElement> Read(HttpResponseMessage response) =>
            await response.Content.ReadFromJsonAsync<JsonElement>();

        protected static Task<HttpResponseMessage> Patch(HttpClient client, string url, string? json) =>
            client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url) { Content = json is null ? null : JsonBody(json) });

        protected static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string? json = null) =>
            client.SendAsync(new HttpRequestMessage(method, url) { Content = json is null ? null : JsonBody(json) });
    }

    public class ApplyEndpointTests : EndpointTestBase
    {
        public ApplyEndpointTests(ApiFactory factory) : base(factory) { }

        // ----- who may call it --------------------------------------------------

        [Fact]
        public async Task Apply_needs_a_login_token()
        {
            var job = Store.AddExternalJob();

            var response = await Factory.CreateClient().PostAsync($"/api/jobs/{job.JobId}/apply", null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain(Store.Applications, a => a.JobId == job.JobId);
        }

        [Theory]
        [InlineData("expired")]
        [InlineData("wrong-key")]
        [InlineData("wrong-audience")]
        [InlineData("garbage")]
        public async Task Apply_rejects_bad_tokens(string kind)
        {
            var job = Store.AddExternalJob();
            var user = NewUser();

            var token = kind switch
            {
                "expired" => ApiFactory.MakeToken(user, lifetime: TimeSpan.FromMinutes(-10)),
                "wrong-key" => ApiFactory.MakeToken(user, key: "some-other-signing-key-that-is-also-long-enough-123"),
                "wrong-audience" => ApiFactory.MakeToken(user, audience: "somebody-else"),
                _ => "not.a.jwt"
            };

            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsync($"/api/jobs/{job.JobId}/apply", null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Employers_cannot_use_the_job_seeker_apply_endpoint()
        {
            var job = Store.AddExternalJob();

            var response = await Client(NewUser(), role: "employer").PostAsync($"/api/jobs/{job.JobId}/apply", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task The_user_comes_from_the_token_never_from_the_request()
        {
            var job = Store.AddExternalJob();
            var me = NewUser();
            var someoneElse = NewUser();

            // Body and query both try to name a different user; neither is read.
            var response = await Client(me).PostAsync(
                $"/api/jobs/{job.JobId}/apply?userId={someoneElse}", JsonBody($"{{\"userId\":{someoneElse}}}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(me, Store.Applications.Single(a => a.JobId == job.JobId).UserId);
        }

        // ----- internal jobs ----------------------------------------------------

        [Fact]
        public async Task Internal_apply_returns_the_application_details()
        {
            var employer = NewUser();
            var job = Store.AddInternalJob(employer);

            var response = await Client(NewUser()).PostAsync($"/api/jobs/{job.JobId}/apply", null);
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("internal", json.GetProperty("type").GetString());
            Assert.Equal("Submitted", json.GetProperty("status").GetString());
            Assert.True(json.GetProperty("applicationId").GetInt32() > 0);
            Assert.False(json.GetProperty("alreadyApplied").GetBoolean());
            Assert.Contains(Store.Notifications, n => n.UserId == employer);
        }

        [Fact]
        public async Task Applying_twice_says_alreadyApplied_with_the_same_application()
        {
            var client = Client(NewUser());
            var job = Store.AddInternalJob(NewUser());

            var first = await Read(await client.PostAsync($"/api/jobs/{job.JobId}/apply", null));
            var second = await Read(await client.PostAsync($"/api/jobs/{job.JobId}/apply", null));

            Assert.False(first.GetProperty("alreadyApplied").GetBoolean());
            Assert.True(second.GetProperty("alreadyApplied").GetBoolean());
            Assert.Equal(first.GetProperty("applicationId").GetInt32(), second.GetProperty("applicationId").GetInt32());
        }

        [Fact]
        public async Task The_21st_internal_application_in_24_hours_gets_429_with_a_clear_message()
        {
            var client = Client(NewUser());
            var jobs = Enumerable.Range(1, 21).Select(i => Store.AddInternalJob(NewUser(), $"Job {i}")).ToList();

            foreach (var job in jobs.Take(20))
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/jobs/{job.JobId}/apply", null)).StatusCode);

            var response = await client.PostAsync($"/api/jobs/{jobs[20].JobId}/apply", null);
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("86400", response.Headers.GetValues("Retry-After").Single());
            Assert.Equal(86400, json.GetProperty("retryAfterSeconds").GetInt32());

            var message = json.GetProperty("message").GetString();
            Assert.Contains("20 applications", message);
            Assert.Contains("24 hours", message);
            Assert.Contains("apply again in about 24 hours", message);
        }

        [Fact]
        public async Task External_redirects_never_trigger_429()
        {
            var client = Client(NewUser());

            for (var i = 0; i < 25; i++)
            {
                var job = Store.AddExternalJob();
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/jobs/{job.JobId}/apply", null)).StatusCode);
            }
        }

        // ----- external jobs ----------------------------------------------------

        [Fact]
        public async Task External_apply_returns_the_redirect_details()
        {
            var job = Store.AddExternalJob("https://www.linkedin.com/jobs/view/123", publisher: "LinkedIn");

            var response = await Client(NewUser()).PostAsync($"/api/jobs/{job.JobId}/apply", null);
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("external", json.GetProperty("type").GetString());
            Assert.Equal("https://www.linkedin.com/jobs/view/123", json.GetProperty("redirectUrl").GetString());
            Assert.Equal("LinkedIn", json.GetProperty("publisher").GetString());
            Assert.Equal("Redirected", json.GetProperty("status").GetString());
            Assert.True(json.GetProperty("applicationId").GetInt32() > 0);
        }

        [Fact]
        public async Task The_redirect_only_ever_comes_from_the_database_never_from_the_client()
        {
            var job = Store.AddExternalJob("https://www.linkedin.com/jobs/view/123");

            // A client tries every way of suggesting its own destination.
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/jobs/{job.JobId}/apply?redirectUrl=https://evil.example&url=https://evil.example")
            {
                Content = JsonBody("{\"redirectUrl\":\"https://evil.example\",\"applyUrl\":\"https://evil.example\",\"url\":\"https://evil.example\"}")
            };
            request.Headers.Add("Referer", "https://evil.example");
            request.Headers.Add("X-Forwarded-Host", "evil.example");

            var response = await Client(NewUser()).SendAsync(request);
            var json = await Read(response);

            Assert.Equal("https://www.linkedin.com/jobs/view/123", json.GetProperty("redirectUrl").GetString());
        }

        [Fact]
        public async Task An_expired_job_returns_410_with_a_friendly_message_and_is_flagged()
        {
            var job = Store.AddExternalJob("https://www.linkedin.com/jobs/expired/9", applyIsDirect: true, publisher: "LinkedIn");
            var user = NewUser();

            var response = await Client(user).PostAsync($"/api/jobs/{job.JobId}/apply", null);
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            Assert.True(json.GetProperty("expired").GetBoolean());
            Assert.Contains("expired", json.GetProperty("message").GetString());
            Assert.True(job.IsExpired);
            Assert.DoesNotContain(Store.Applications, a => a.UserId == user);
        }

        [Fact]
        public async Task Clicking_twice_returns_the_same_application_and_link()
        {
            var client = Client(NewUser());
            var job = Store.AddExternalJob();

            var first = await Read(await client.PostAsync($"/api/jobs/{job.JobId}/apply", null));
            var second = await Read(await client.PostAsync($"/api/jobs/{job.JobId}/apply", null));

            Assert.Equal(first.GetProperty("applicationId").GetInt32(), second.GetProperty("applicationId").GetInt32());
            Assert.Equal(first.GetProperty("redirectUrl").GetString(), second.GetProperty("redirectUrl").GetString());
        }

        [Fact]
        public async Task Unknown_jobs_are_404()
        {
            var response = await Client(NewUser()).PostAsync("/api/jobs/9999999/apply", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task A_job_id_that_is_not_a_number_does_not_match_the_route()
        {
            var response = await Client(NewUser()).PostAsync("/api/jobs/abc/apply", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Apply_is_POST_only()
        {
            var job = Store.AddExternalJob();

            var response = await Client(NewUser()).GetAsync($"/api/jobs/{job.JobId}/apply");

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    public class ConfirmEndpointTests : EndpointTestBase
    {
        public ConfirmEndpointTests(ApiFactory factory) : base(factory) { }

        private async Task<int> Redirect(HttpClient client)
        {
            var job = Store.AddExternalJob();
            var json = await Read(await client.PostAsync($"/api/jobs/{job.JobId}/apply", null));
            return json.GetProperty("applicationId").GetInt32();
        }

        [Fact]
        public async Task Applied_true_marks_it_Applied_Externally()
        {
            var client = Client(NewUser());
            var id = await Redirect(client);

            var response = await Patch(client, $"/api/applications/{id}/confirm-external", "{\"applied\":true}");
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Applied Externally", json.GetProperty("status").GetString());
            Assert.NotEqual(JsonValueKind.Null, json.GetProperty("confirmedAt").ValueKind);
            Assert.Equal("Applied Externally", Store.GetApplication(id)!.Status);
        }

        [Fact]
        public async Task Applied_false_leaves_it_Redirected()
        {
            var client = Client(NewUser());
            var id = await Redirect(client);

            var response = await Patch(client, $"/api/applications/{id}/confirm-external", "{\"applied\":false}");
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Redirected", json.GetProperty("status").GetString());
            Assert.Equal("Redirected", Store.GetApplication(id)!.Status);
            Assert.Null(Store.GetApplication(id)!.ConfirmedAt);
        }

        [Fact]
        public async Task Another_user_cannot_confirm_it()
        {
            var owner = Client(NewUser());
            var id = await Redirect(owner);

            var response = await Patch(Client(NewUser()), $"/api/applications/{id}/confirm-external", "{\"applied\":true}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Redirected", Store.GetApplication(id)!.Status);
        }

        [Fact]
        public async Task It_can_only_be_confirmed_while_Redirected()
        {
            var client = Client(NewUser());
            var id = await Redirect(client);

            await Patch(client, $"/api/applications/{id}/confirm-external", "{\"applied\":true}");
            var again = await Patch(client, $"/api/applications/{id}/confirm-external", "{\"applied\":true}");
            var json = await Read(again);

            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal("Applied Externally", json.GetProperty("status").GetString());
        }

        [Fact]
        public async Task An_internal_application_cannot_be_confirmed()
        {
            var client = Client(NewUser());
            var job = Store.AddInternalJob(NewUser());
            var id = (await Read(await client.PostAsync($"/api/jobs/{job.JobId}/apply", null))).GetProperty("applicationId").GetInt32();

            var response = await Patch(client, $"/api/applications/{id}/confirm-external", "{\"applied\":true}");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Submitted", Store.GetApplication(id)!.Status);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{}")]
        [InlineData("null")]
        [InlineData("{\"applied\":null}")]
        [InlineData("{\"applied\":\"yes\"}")]
        [InlineData("not json")]
        public async Task The_body_must_say_applied_true_or_false(string? body)
        {
            var client = Client(NewUser());
            var id = await Redirect(client);

            var response = await Patch(client, $"/api/applications/{id}/confirm-external", body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Redirected", Store.GetApplication(id)!.Status);
        }

        [Fact]
        public async Task Confirming_needs_a_login_and_the_job_seeker_role()
        {
            var owner = Client(NewUser());
            var id = await Redirect(owner);

            Assert.Equal(HttpStatusCode.Unauthorized,
                (await Patch(Factory.CreateClient(), $"/api/applications/{id}/confirm-external", "{\"applied\":true}")).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await Patch(Client(NewUser(), "employer"), $"/api/applications/{id}/confirm-external", "{\"applied\":true}")).StatusCode);
        }

        [Fact]
        public async Task An_unknown_application_is_404()
        {
            var response = await Patch(Client(NewUser()), "/api/applications/9999999/confirm-external", "{\"applied\":true}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Only_PATCH_is_accepted()
        {
            var client = Client(NewUser());
            var id = await Redirect(client);

            var response = await Send(client, HttpMethod.Post, $"/api/applications/{id}/confirm-external", "{\"applied\":true}");

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }
}
