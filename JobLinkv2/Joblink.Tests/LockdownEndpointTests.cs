using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JobLinkv2.Models;
using Joblink.Security;
using Joblink.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Joblink.Tests
{
    // /api/Application and /api/Joblisting used to accept anything from anyone.
    // These prove they can no longer be used to forge or rewrite applications, or
    // to change where the Apply button sends people.
    public class ApplicationEndpointTests : EndpointTestBase
    {
        public ApplicationEndpointTests(ApiFactory factory) : base(factory) { }

        private JoblistingModel ManualJob() =>
            Store.AddListing(new JoblistingModel { Title = "QA Tester", Company = "Acme", SourceApi = "manual" });

        private ApplicationModel ManualApplication(int userId, string status = "Applied") =>
            Store.AddApplicationRow(new ApplicationModel
            {
                UserId = userId, JobId = ManualJob().JobId, Status = status,
                ApplicationType = "External", AppliedAt = Factory.Clock.UtcNow
            });

        // ----- login required ---------------------------------------------------

        [Theory]
        [InlineData("GET", "/api/Application")]
        [InlineData("GET", "/api/Application/1")]
        [InlineData("GET", "/api/Application/by-user/1")]
        [InlineData("POST", "/api/Application")]
        [InlineData("PUT", "/api/Application")]
        [InlineData("DELETE", "/api/Application?id=1")]
        public async Task Every_application_endpoint_needs_a_login(string method, string url)
        {
            var response = await Send(Factory.CreateClient(), new HttpMethod(method), url, method is "POST" or "PUT" ? "{}" : null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("GET", "/api/Application")]
        [InlineData("POST", "/api/Application")]
        [InlineData("PUT", "/api/Application")]
        [InlineData("DELETE", "/api/Application?id=1")]
        public async Task Employers_have_no_access_to_the_job_seeker_tracker(string method, string url)
        {
            var response = await Send(Client(NewUser(), "employer"), new HttpMethod(method), url, method is "POST" or "PUT" ? "{}" : null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ----- reading ----------------------------------------------------------

        [Fact]
        public async Task GET_all_now_returns_only_the_callers_applications()
        {
            var me = NewUser();
            var other = NewUser();
            var mine = ManualApplication(me);
            ManualApplication(other);

            var json = await Read(await Client(me).GetAsync("/api/Application"));

            var ids = json.EnumerateArray().Select(a => a.GetProperty("applicationId").GetInt32()).ToList();
            Assert.Equal(new[] { mine.ApplicationId }, ids);
        }

        [Fact]
        public async Task by_user_only_works_for_yourself()
        {
            var me = NewUser();
            var other = NewUser();
            ManualApplication(me);

            Assert.Equal(HttpStatusCode.OK, (await Client(me).GetAsync($"/api/Application/by-user/{me}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(me).GetAsync($"/api/Application/by-user/{other}")).StatusCode);
        }

        [Fact]
        public async Task The_response_carries_the_new_apply_fields()
        {
            var me = NewUser();
            ManualApplication(me);

            var first = (await Read(await Client(me).GetAsync($"/api/Application/by-user/{me}"))).EnumerateArray().First();

            Assert.Equal("External", first.GetProperty("applicationType").GetString());
            Assert.True(first.TryGetProperty("redirectedAt", out _));
            Assert.True(first.TryGetProperty("confirmedAt", out _));
        }

        [Fact]
        public async Task GET_by_id_is_owner_only()
        {
            var me = NewUser();
            var application = ManualApplication(me);

            Assert.Equal(HttpStatusCode.OK, (await Client(me).GetAsync($"/api/Application/{application.ApplicationId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(NewUser()).GetAsync($"/api/Application/{application.ApplicationId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Client(me).GetAsync("/api/Application/9999999")).StatusCode);
        }

        // ----- creating (the tracker's "Log Application") ------------------------

        [Fact]
        public async Task POST_logs_a_manual_application_for_the_caller_only()
        {
            var me = NewUser();
            var someoneElse = NewUser();
            var job = ManualJob();

            var response = await Client(me).PostAsync("/api/Application", JsonBody(
                $"{{\"userId\":{someoneElse},\"jobId\":{job.JobId},\"status\":\"Interview\",\"applicationType\":\"Internal\",\"resumeId\":999}}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var saved = Store.Applications.Single(a => a.JobId == job.JobId);
            Assert.Equal(me, saved.UserId);                   // not the user named in the body
            Assert.Equal("External", saved.ApplicationType);  // can't claim to be an internal application
            Assert.Equal("Interview", saved.Status);
        }

        [Theory]
        [InlineData("Submitted")]
        [InlineData("Viewed")]
        [InlineData("Shortlisted")]
        [InlineData("Redirected")]
        [InlineData("Applied Externally")]
        public async Task POST_cannot_forge_a_status_the_apply_flow_owns(string status)
        {
            var me = NewUser();
            var job = ManualJob();

            var response = await Client(me).PostAsync("/api/Application", JsonBody($"{{\"jobId\":{job.JobId},\"status\":\"{status}\"}}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain(Store.Applications, a => a.UserId == me);
        }

        [Fact]
        public async Task POST_cannot_be_used_to_apply_to_a_real_job_around_the_apply_limit()
        {
            var me = NewUser();
            var employerJob = Store.AddInternalJob(NewUser());
            var importedJob = Store.AddExternalJob();

            foreach (var job in new[] { employerJob, importedJob })
            {
                var response = await Client(me).PostAsync("/api/Application", JsonBody($"{{\"jobId\":{job.JobId},\"status\":\"Applied\"}}"));

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            Assert.DoesNotContain(Store.Applications, a => a.UserId == me);
        }

        [Fact]
        public async Task POST_twice_for_the_same_job_is_a_conflict()
        {
            var me = NewUser();
            var job = ManualJob();
            var body = JsonBody($"{{\"jobId\":{job.JobId},\"status\":\"Applied\"}}");

            Assert.Equal(HttpStatusCode.OK, (await Client(me).PostAsync("/api/Application", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Client(me).PostAsync("/api/Application", JsonBody($"{{\"jobId\":{job.JobId},\"status\":\"Offer\"}}"))).StatusCode);
        }

        // ----- changing status --------------------------------------------------

        [Fact]
        public async Task PUT_changes_a_manual_applications_status()
        {
            var me = NewUser();
            var application = ManualApplication(me, "Applied");

            var response = await Client(me).PutAsync("/api/Application", JsonBody(
                $"{{\"applicationId\":{application.ApplicationId},\"status\":\"Offer\"}}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Offer", Store.GetApplication(application.ApplicationId)!.Status);
        }

        [Fact]
        public async Task PUT_only_changes_the_status_even_if_the_body_tries_more()
        {
            var me = NewUser();
            var application = ManualApplication(me, "Applied");
            var before = (application.UserId, application.JobId, application.ApplicationType, application.AppliedAt);

            await Client(me).PutAsync("/api/Application", JsonBody(
                $"{{\"applicationId\":{application.ApplicationId},\"status\":\"Interview\",\"userId\":1,\"jobId\":2," +
                "\"applicationType\":\"Internal\",\"appliedAt\":\"2000-01-01T00:00:00Z\",\"redirectedAt\":\"2000-01-01T00:00:00Z\"," +
                "\"confirmedAt\":\"2000-01-01T00:00:00Z\",\"isDeleted\":true}"));

            var after = Store.GetApplication(application.ApplicationId)!;
            Assert.Equal("Interview", after.Status);
            Assert.Equal(before, (after.UserId, after.JobId, after.ApplicationType, after.AppliedAt));
            Assert.Null(after.RedirectedAt);
            Assert.Null(after.ConfirmedAt);
            Assert.False(after.IsDeleted);
        }

        [Fact]
        public async Task PUT_cannot_touch_someone_elses_application()
        {
            var application = ManualApplication(NewUser(), "Applied");

            var response = await Client(NewUser()).PutAsync("/api/Application", JsonBody(
                $"{{\"applicationId\":{application.ApplicationId},\"status\":\"Offer\"}}"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Applied", Store.GetApplication(application.ApplicationId)!.Status);
        }

        [Theory]
        [InlineData("Submitted")]
        [InlineData("Shortlisted")]
        [InlineData("Redirected")]
        [InlineData("Applied Externally")]
        [InlineData("Whatever")]
        public async Task PUT_cannot_set_a_status_outside_the_trackers_own(string status)
        {
            var me = NewUser();
            var application = ManualApplication(me, "Applied");

            var response = await Client(me).PutAsync("/api/Application", JsonBody(
                $"{{\"applicationId\":{application.ApplicationId},\"status\":\"{status}\"}}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Applied", Store.GetApplication(application.ApplicationId)!.Status);
        }

        [Fact]
        public async Task PUT_cannot_edit_an_internal_application_the_employer_owns_its_status()
        {
            var me = NewUser();
            var job = Store.AddInternalJob(NewUser());
            var id = (await Read(await Client(me).PostAsync($"/api/jobs/{job.JobId}/apply", null))).GetProperty("applicationId").GetInt32();

            var response = await Client(me).PutAsync("/api/Application", JsonBody($"{{\"applicationId\":{id},\"status\":\"Offer\"}}"));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Submitted", Store.GetApplication(id)!.Status);
        }

        [Fact]
        public async Task PUT_cannot_edit_a_redirect_application_it_moves_only_through_confirm()
        {
            var me = NewUser();
            var job = Store.AddExternalJob();
            var id = (await Read(await Client(me).PostAsync($"/api/jobs/{job.JobId}/apply", null))).GetProperty("applicationId").GetInt32();

            var response = await Client(me).PutAsync("/api/Application", JsonBody($"{{\"applicationId\":{id},\"status\":\"Offer\"}}"));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Redirected", Store.GetApplication(id)!.Status);
        }

        // ----- withdrawing ------------------------------------------------------

        [Fact]
        public async Task DELETE_is_owner_only()
        {
            var me = NewUser();
            var application = ManualApplication(me);

            Assert.Equal(HttpStatusCode.Forbidden, (await Client(NewUser()).DeleteAsync($"/api/Application?id={application.ApplicationId}")).StatusCode);
            Assert.NotNull(Store.GetApplication(application.ApplicationId));

            Assert.Equal(HttpStatusCode.OK, (await Client(me).DeleteAsync($"/api/Application?id={application.ApplicationId}")).StatusCode);
            Assert.Null(Store.GetApplication(application.ApplicationId));

            Assert.Equal(HttpStatusCode.NotFound, (await Client(me).DeleteAsync($"/api/Application?id={application.ApplicationId}")).StatusCode);
        }

        // ----- job listings -----------------------------------------------------

        [Theory]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public async Task Job_listings_cannot_be_edited_or_deleted_by_anyone_through_the_API(string method)
        {
            var listing = Store.AddExternalJob("https://www.linkedin.com/jobs/1");
            var url = method == "DELETE" ? $"/api/Joblisting?id={listing.JobId}" : "/api/Joblisting";
            var body = method == "PUT" ? $"{{\"jobId\":{listing.JobId},\"applyUrl\":\"https://evil.example\"}}" : null;

            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(Factory.CreateClient(), new HttpMethod(method), url, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(Client(NewUser()), new HttpMethod(method), url, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(Client(NewUser(), "employer"), new HttpMethod(method), url, body)).StatusCode);

            Assert.Equal("https://www.linkedin.com/jobs/1", listing.ApplyUrl);   // still what the import stored
            Assert.False(listing.IsDeleted);
        }

        [Fact]
        public async Task Adding_a_job_by_hand_needs_a_job_seeker_login()
        {
            var body = "{\"title\":\"QA\",\"company\":\"Acme\"}";

            Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().PostAsync("/api/Joblisting", JsonBody(body))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(NewUser(), "employer").PostAsync("/api/Joblisting", JsonBody(body))).StatusCode);
        }

        [Fact]
        public async Task A_job_added_by_hand_cannot_carry_an_apply_link_an_employer_or_a_source()
        {
            var response = await Client(NewUser()).PostAsync("/api/Joblisting", JsonBody(
                "{\"title\":\"QA Tester\",\"company\":\"Acme\",\"location\":\"Manila\",\"source\":\"Internal\",\"employerId\":7," +
                "\"sourceApi\":\"jsearch\",\"applyUrl\":\"https://evil.example/phish\",\"applyIsDirect\":true,\"publisher\":\"LinkedIn\"," +
                "\"applyOptions\":\"[{\\\"apply_link\\\":\\\"https://evil.example\\\",\\\"is_direct\\\":true}]\",\"isExpired\":true}"));

            var json = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(json.GetProperty("jobId").GetInt32() > 0);         // the tracker uses this id
            Assert.Equal("manual", json.GetProperty("sourceApi").GetString());
            Assert.Equal("External", json.GetProperty("source").GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("employerId").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("applyUrl").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("publisher").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("applyOptions").ValueKind);
            Assert.False(json.GetProperty("applyIsDirect").GetBoolean());
            Assert.False(json.GetProperty("isExpired").GetBoolean());
        }

        [Fact]
        public async Task A_job_added_by_hand_needs_a_title_and_company()
        {
            var response = await Client(NewUser()).PostAsync("/api/Joblisting", JsonBody("{\"title\":\"\",\"company\":\"Acme\"}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    public class JwtTests : EndpointTestBase
    {
        public JwtTests(ApiFactory factory) : base(factory) { }

        [Fact]
        public async Task Tokens_issued_by_the_token_service_are_accepted_by_the_API()
        {
            var service = new JwtTokenService(new JwtOptions { Key = ApiFactory.SigningKey }, TimeProvider.System);
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", service.CreateToken(NewUser(), "user"));

            var response = await client.GetAsync("/api/Application");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public void A_token_carries_only_the_user_id_and_role()
        {
            var service = new JwtTokenService(new JwtOptions { Key = ApiFactory.SigningKey }, TimeProvider.System);

            var token = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateToken(42, "employer"));

            Assert.Equal("42", token.Claims.Single(c => c.Type == "sub").Value);
            Assert.Equal("employer", token.Claims.Single(c => c.Type == "role").Value);
            Assert.DoesNotContain(token.Claims, c => c.Type is "email" or "unique_name" or "name" or "password");
            Assert.True(token.ValidTo > DateTime.UtcNow.AddHours(7));   // 8-hour default lifetime
            Assert.True(token.ValidTo < DateTime.UtcNow.AddHours(9));
        }

        [Fact]
        public void The_user_id_is_read_back_from_the_token_claims()
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(
                ApiFactory.MakeToken(77),
                new JwtOptions { Key = ApiFactory.SigningKey }.ToValidationParameters(),
                out _);

            Assert.Equal(77, principal.GetUserId());
        }

        // ----- where the signing key comes from ---------------------------------

        private sealed class Env : IHostEnvironment
        {
            public Env(string name) => EnvironmentName = name;
            public string EnvironmentName { get; set; }
            public string ApplicationName { get; set; } = "Joblink";
            public string ContentRootPath { get; set; } = ".";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static IConfiguration Config(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

        [Fact]
        public void The_key_is_read_from_configuration()
        {
            var options = JwtOptions.From(Config(("Jwt:Key", new string('k', 40))), new Env("Production"));

            Assert.Equal(new string('k', 40), options.Key);
        }

        [Fact]
        public void Production_refuses_to_start_without_a_key()
        {
            var error = Assert.Throws<InvalidOperationException>(() => JwtOptions.From(Config(), new Env("Production")));

            Assert.Contains("Jwt__Key", error.Message);
        }

        [Fact]
        public void A_weak_key_is_refused_in_every_environment()
        {
            Assert.Throws<InvalidOperationException>(() => JwtOptions.From(Config(("Jwt:Key", "too-short")), new Env("Production")));
            Assert.Throws<InvalidOperationException>(() => JwtOptions.From(Config(("Jwt:Key", "too-short")), new Env("Development")));
        }

        [Fact]
        public void Development_gets_a_random_throwaway_key_when_none_is_set()
        {
            var first = JwtOptions.From(Config(), new Env("Development"));
            var second = JwtOptions.From(Config(), new Env("Development"));

            Assert.True(first.Key.Length >= JwtOptions.MinKeyLength);
            Assert.NotEqual(first.Key, second.Key);
        }
    }
}
