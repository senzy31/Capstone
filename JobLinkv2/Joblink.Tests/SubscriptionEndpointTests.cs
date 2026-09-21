using System.Net;
using System.Text.Json;
using Joblink.Services.Subscriptions;
using JobLinkv2.Services.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Joblink.Tests
{
    // The plan endpoints through real routing and real JWT checks, on the tests' clock.
    // The plan is read from the store on every request - never from the login token.
    public class SubscriptionEndpointTests : EndpointTestBase
    {
        public SubscriptionEndpointTests(Support.ApiFactory factory) : base(factory) { }

        private DateTime Now => Factory.Clock.UtcNow;

        private static DateTime Utc(JsonElement element) => element.GetDateTimeOffset().UtcDateTime;

        private static Task<HttpResponseMessage> Upgrade(HttpClient client, string billing = "Monthly") =>
            Send(client, HttpMethod.Post, "/api/Subscription/upgrade", $"{{\"billing\":\"{billing}\"}}");

        private static Task<HttpResponseMessage> GetPlan(HttpClient client) => Send(client, HttpMethod.Get, "/api/Subscription");

        private static Task<HttpResponseMessage> Cancel(HttpClient client) => Send(client, HttpMethod.Post, "/api/Subscription/cancel");

        // ----- who may call ------------------------------------------------------

        [Theory]
        [InlineData("GET", "/api/Subscription")]
        [InlineData("POST", "/api/Subscription/upgrade")]
        [InlineData("POST", "/api/Subscription/cancel")]
        public async Task Every_endpoint_needs_a_login(string method, string url)
        {
            var response = await Send(Factory.CreateClient(), new HttpMethod(method), url, method == "POST" ? "{\"billing\":\"Monthly\"}" : null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("GET", "/api/Subscription")]
        [InlineData("POST", "/api/Subscription/upgrade")]
        [InlineData("POST", "/api/Subscription/cancel")]
        public async Task Employers_have_no_plans_and_cannot_buy_one(string method, string url)
        {
            var employer = NewUser();

            var response = await Send(Client(employer, "employer"), new HttpMethod(method), url, method == "POST" ? "{\"billing\":\"Monthly\"}" : null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Null(Factory.Subscriptions.Get(employer));
        }

        // ----- reading -----------------------------------------------------------

        [Fact]
        public async Task A_new_job_seeker_is_on_free_with_free_limits_and_ads()
        {
            var user = NewUser();
            Factory.Usage.Set(user, resumes: 1, savedJobs: 4);

            var body = await Read(await GetPlan(Client(user)));

            Assert.Equal("Free", body.GetProperty("plan").GetString());
            Assert.False(body.GetProperty("isPremium").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("billing").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.GetProperty("premiumUntil").ValueKind);
            Assert.False(body.GetProperty("cancelled").GetBoolean());

            Assert.Equal(1, body.GetProperty("limits").GetProperty("resumeVersions").GetInt32());
            Assert.Equal(10, body.GetProperty("limits").GetProperty("savedJobs").GetInt32());
            Assert.Equal(1, body.GetProperty("usage").GetProperty("resumeVersions").GetInt32());
            Assert.Equal(4, body.GetProperty("usage").GetProperty("savedJobs").GetInt32());

            var features = body.GetProperty("features");

            Assert.True(features.GetProperty("showAds").GetBoolean());
            Assert.False(features.GetProperty("detailedScore").GetBoolean());
            Assert.False(features.GetProperty("missingSkills").GetBoolean());
            Assert.False(features.GetProperty("advancedTemplates").GetBoolean());
            Assert.False(features.GetProperty("priorityApplication").GetBoolean());
        }

        [Fact]
        public async Task The_answer_lists_the_prices_and_says_the_checkout_is_a_demo()
        {
            var body = await Read(await GetPlan(Client(NewUser())));

            Assert.Equal(new[] { ("Monthly", 1, 99), ("Quarterly", 3, 249), ("Annual", 12, 899) },
                body.GetProperty("plans").EnumerateArray()
                    .Select(p => (p.GetProperty("billing").GetString()!, p.GetProperty("months").GetInt32(), p.GetProperty("pricePhp").GetInt32())).ToArray());
            Assert.True(body.GetProperty("demoCheckout").GetBoolean());
        }

        [Fact]
        public async Task The_answer_has_exactly_these_fields()
        {
            var body = await Read(await GetPlan(Client(NewUser())));

            Assert.Equal(
                new[] { "billing", "cancelled", "demoCheckout", "features", "isPremium", "limits", "plan", "plans", "premiumStartedAt", "premiumUntil", "usage" },
                body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        // ----- upgrading ---------------------------------------------------------

        [Theory]
        [InlineData("Monthly", 1)]
        [InlineData("Quarterly", 3)]
        [InlineData("Annual", 12)]
        public async Task Upgrading_gives_premium_for_one_three_or_twelve_months(string billing, int months)
        {
            var user = Client(NewUser());

            var response = await Upgrade(user, billing);
            var body = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Premium", body.GetProperty("plan").GetString());
            Assert.True(body.GetProperty("isPremium").GetBoolean());
            Assert.Equal(billing, body.GetProperty("billing").GetString());
            Assert.Equal(Now.AddMonths(months), Utc(body.GetProperty("premiumUntil")));
            Assert.Equal(Now, Utc(body.GetProperty("premiumStartedAt")));

            // ...and it is what a fresh read says too
            Assert.Equal(Now.AddMonths(months), Utc((await Read(await GetPlan(user))).GetProperty("premiumUntil")));
        }

        [Fact]
        public async Task The_dates_are_sent_as_utc_so_browsers_do_not_read_them_as_local_time()
        {
            var response = await Upgrade(Client(NewUser()));
            var text = (await Read(response)).GetProperty("premiumUntil").GetString()!;

            Assert.EndsWith("Z", text);
        }

        [Fact]
        public async Task An_upgrade_takes_effect_at_once_with_the_same_login_token()
        {
            // One client = one token, created before the upgrade. If the plan were in the
            // token this would still say Free afterwards.
            var user = Client(NewUser());

            Assert.Equal("Free", (await Read(await GetPlan(user))).GetProperty("plan").GetString());

            await Upgrade(user);

            var after = await Read(await GetPlan(user));

            Assert.Equal("Premium", after.GetProperty("plan").GetString());
            Assert.False(after.GetProperty("features").GetProperty("showAds").GetBoolean());
            Assert.Equal(10, after.GetProperty("limits").GetProperty("resumeVersions").GetInt32());
            Assert.Equal(JsonValueKind.Null, after.GetProperty("limits").GetProperty("savedJobs").ValueKind);   // unlimited
        }

        [Fact]
        public async Task Premium_gets_every_premium_feature_and_no_ads()
        {
            var user = Client(NewUser());
            await Upgrade(user);

            var features = (await Read(await GetPlan(user))).GetProperty("features");

            Assert.False(features.GetProperty("showAds").GetBoolean());
            Assert.True(features.GetProperty("detailedScore").GetBoolean());
            Assert.True(features.GetProperty("missingSkills").GetBoolean());
            Assert.True(features.GetProperty("advancedTemplates").GetBoolean());
            Assert.True(features.GetProperty("priorityApplication").GetBoolean());
        }

        [Fact]
        public async Task Upgrading_while_premium_adds_time_after_the_current_end()
        {
            var user = Client(NewUser());

            await Upgrade(user, "Monthly");
            var second = await Read(await Upgrade(user, "Quarterly"));

            Assert.Equal(Now.AddMonths(1).AddMonths(3), Utc(second.GetProperty("premiumUntil")));
            Assert.Equal(Now, Utc(second.GetProperty("premiumStartedAt")));
            Assert.Equal("Quarterly", second.GetProperty("billing").GetString());
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"billing\":null}")]
        [InlineData("{\"billing\":\"\"}")]
        [InlineData("{\"billing\":\"Weekly\"}")]
        [InlineData("{\"billing\":\"Lifetime\"}")]
        [InlineData("{\"months\":12}")]
        public async Task Anything_but_a_real_billing_period_is_a_400_and_changes_nothing(string body)
        {
            var user = NewUser();

            var response = await Send(Client(user), HttpMethod.Post, "/api/Subscription/upgrade", body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(Factory.Subscriptions.Get(user));
        }

        [Fact]
        public async Task The_billing_period_is_accepted_in_any_capitals()
        {
            var response = await Upgrade(Client(NewUser()), "annual");

            Assert.Equal("Annual", (await Read(response)).GetProperty("billing").GetString());
        }

        // The request is the only thing a client sends, and it can only name a billing period.
        [Fact]
        public async Task A_request_cannot_name_its_own_plan_expiry_start_or_another_user()
        {
            var me = NewUser();
            var someoneElse = NewUser();

            var response = await Send(Client(me), HttpMethod.Post, "/api/Subscription/upgrade", $$"""
                {
                  "billing": "Monthly",
                  "plan": "Premium",
                  "premiumUntil": "2099-12-31T00:00:00Z",
                  "premiumStartedAt": "2000-01-01T00:00:00Z",
                  "cancelledAt": null,
                  "userId": {{someoneElse}},
                  "pricePhp": 0
                }
                """);

            var body = await Read(response);

            Assert.Equal(Now.AddMonths(1), Utc(body.GetProperty("premiumUntil")));   // the server's date, not 2099
            Assert.Equal(Now, Utc(body.GetProperty("premiumStartedAt")));
            Assert.Null(Factory.Subscriptions.Get(someoneElse));
        }

        [Fact]
        public async Task Demo_checkout_can_be_switched_off()
        {
            var options = Factory.Services.GetRequiredService<SubscriptionOptions>();
            var user = NewUser();

            options.DemoCheckout = false;

            try
            {
                var upgrade = await Upgrade(Client(user));
                var read = await GetPlan(Client(user));
                var body = await Read(read);

                Assert.Equal(HttpStatusCode.Forbidden, upgrade.StatusCode);
                Assert.Equal("demo_checkout_off", (await Read(upgrade)).GetProperty("code").GetString());
                Assert.Null(Factory.Subscriptions.Get(user));
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                Assert.False(body.GetProperty("demoCheckout").GetBoolean());
            }
            finally
            {
                options.DemoCheckout = true;
            }
        }

        // ----- cancelling --------------------------------------------------------

        [Fact]
        public async Task Cancelling_keeps_premium_until_the_end_date_then_it_is_free()
        {
            var user = Client(NewUser());
            await Upgrade(user, "Monthly");

            var cancelled = await Cancel(user);
            var body = await Read(cancelled);

            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.Equal("Premium", body.GetProperty("plan").GetString());     // still Premium today
            Assert.True(body.GetProperty("cancelled").GetBoolean());
            Assert.Equal(Now.AddMonths(1), Utc(body.GetProperty("premiumUntil")));

            Factory.Clock.Advance(TimeSpan.FromDays(32));

            var after = await Read(await GetPlan(user));

            Assert.Equal("Free", after.GetProperty("plan").GetString());
            Assert.False(after.GetProperty("cancelled").GetBoolean());
        }

        [Fact]
        public async Task Cancelling_twice_is_fine()
        {
            var user = Client(NewUser());
            await Upgrade(user);
            await Cancel(user);

            var again = await Cancel(user);

            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.True((await Read(again)).GetProperty("cancelled").GetBoolean());
        }

        [Fact]
        public async Task There_is_nothing_to_cancel_on_the_free_plan()
        {
            var response = await Cancel(Client(NewUser()));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("no_active_plan", (await Read(response)).GetProperty("code").GetString());
        }

        [Fact]
        public async Task Buying_again_takes_back_a_cancellation()
        {
            var user = Client(NewUser());
            await Upgrade(user);
            await Cancel(user);

            var body = await Read(await Upgrade(user, "Quarterly"));

            Assert.False(body.GetProperty("cancelled").GetBoolean());
        }

        // ----- expiry ------------------------------------------------------------

        [Fact]
        public async Task When_premium_runs_out_the_user_is_free_again_without_anything_running()
        {
            var user = Client(NewUser());
            await Upgrade(user, "Monthly");

            Assert.True((await Read(await GetPlan(user))).GetProperty("isPremium").GetBoolean());

            Factory.Clock.Advance(TimeSpan.FromDays(31));

            var body = await Read(await GetPlan(user));

            Assert.Equal("Free", body.GetProperty("plan").GetString());
            Assert.False(body.GetProperty("isPremium").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("billing").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, body.GetProperty("premiumUntil").ValueKind);   // when it ended is still shown
            Assert.True(body.GetProperty("features").GetProperty("showAds").GetBoolean());       // ads are back
            Assert.Equal(1, body.GetProperty("limits").GetProperty("resumeVersions").GetInt32());
            Assert.Equal(10, body.GetProperty("limits").GetProperty("savedJobs").GetInt32());
        }

        [Fact]
        public async Task A_lapsed_user_can_buy_again_and_starts_fresh()
        {
            var user = NewUser();
            Factory.Subscriptions.Set(new SubscriptionRecord(user, "Premium", "Annual", Now.AddYears(-1).AddDays(-3), Now.AddDays(-3), null));

            var status = await Read(await GetPlan(Client(user)));

            Assert.Equal("Free", status.GetProperty("plan").GetString());

            var body = await Read(await Upgrade(Client(user), "Monthly"));

            Assert.Equal(Now, Utc(body.GetProperty("premiumStartedAt")));
            Assert.Equal(Now.AddMonths(1), Utc(body.GetProperty("premiumUntil")));
        }
    }
}
