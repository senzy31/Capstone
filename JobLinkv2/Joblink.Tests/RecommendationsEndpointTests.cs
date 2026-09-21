using System.Net;
using System.Text.Json;
using JobLinkv2.Services.Matching;
using JobLinkv2.Services.Subscriptions;
using Xunit;

namespace Joblink.Tests
{
    // GET /api/Recommendations through real routing, real JWT checks, the real scorer and the real plan
    // rules - with the job feed and the resume as fakes. The point of most of these: what a Free plan is
    // sent, what a Premium plan is sent, and that nothing the caller sends changes either.
    public class RecommendationsEndpointTests : EndpointTestBase
    {
        public RecommendationsEndpointTests(Support.ApiFactory factory) : base(factory) { }

        private DateTime Now => Factory.Clock.UtcNow;

        // ----- helpers ----------------------------------------------------------------------------------------

        private static Dictionary<string, object?> Job(int id, string title, string description, string city = "Makati", double? min = null, double? max = null,
            string? period = null, string country = "PH", bool remote = false) => new()
        {
            ["job_id"] = $"jsearch-{id}",
            ["joblink_job_id"] = id,
            ["joblink_source"] = "External",
            ["job_title"] = title,
            ["employer_name"] = $"Company {id}",
            ["job_description"] = description,
            ["job_city"] = city,
            ["job_state"] = "Metro Manila",
            ["job_country"] = country,
            ["job_is_remote"] = remote,
            ["job_min_salary"] = min,
            ["job_max_salary"] = max,
            ["job_salary_period"] = period,
            ["job_apply_link"] = $"https://www.linkedin.com/jobs/view/{id}",
        };

        // The dashboard's hand-worked fixture: a React + SQL developer who wants Makati and at least 30,000.
        private static readonly ScoringProfile Maria = new(
            new[] { "React", "SQL" },
            new ScoringPreferences("Makati", null, 30000, 60000),
            "Frontend Developer");

        private static object[] MariaJobs() => new object[]
        {
            Job(3, "Chef", "Lots of cooking.", city: "Manila", min: 20000, max: 25000, period: "MONTH"),          // 17
            Job(1, "React SQL Developer", "We use React and SQL every day.", min: 40000, max: 50000, period: "MONTH"),   // 100
            Job(2, "Data Analyst", "Uses SQL daily.", city: "Cebu"),                                                  // 38
        };

        private int SeekerWith(ScoringProfile profile, params object[] jobs)
        {
            var user = NewUser();

            Factory.Profiles.Set(user, profile);
            Factory.Search.Returns(jobs);

            return user;
        }

        private void MakePremium(int user, int days = 30) =>
            Factory.Subscriptions.Set(new SubscriptionRecord(user, "Premium", "Monthly", Now.AddDays(-1), Now.AddDays(days), null));

        private static Task<HttpResponseMessage> Recommend(HttpClient client, string query = "") => client.GetAsync("/api/Recommendations" + query);

        private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        private static JsonElement[] Jobs(JsonElement body) => body.GetProperty("data").EnumerateArray().ToArray();

        private static int[] Scores(JsonElement body) => Jobs(body).Select(j => j.GetProperty("joblink_match").GetProperty("score").GetInt32()).ToArray();

        // ----- who may ask ---------------------------------------------------------------------------------------

        [Fact]
        public async Task Recommendations_need_a_login()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().GetAsync("/api/Recommendations")).StatusCode);
        }

        [Theory]
        [InlineData("expired")]
        [InlineData("wrong-key")]
        public async Task A_bad_token_is_no_better_than_none(string kind)
        {
            var token = kind == "expired"
                ? Support.ApiFactory.MakeToken(NewUser(), lifetime: TimeSpan.FromMinutes(-10))
                : Support.ApiFactory.MakeToken(NewUser(), key: "some-other-signing-key-that-is-also-long-enough-123");

            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.Unauthorized, (await Recommend(client)).StatusCode);
        }

        [Fact]
        public async Task An_employer_has_no_recommendations_and_costs_no_search()
        {
            var searches = Factory.Search.Searches.Count;

            var response = await Recommend(Client(NewUser(), role: "employer"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(searches, Factory.Search.Searches.Count);
        }

        // ----- what a Free plan is sent ----------------------------------------------------------------------------

        [Fact]
        public async Task A_free_job_seeker_gets_each_jobs_overall_score_and_band_and_nothing_more()
        {
            var user = SeekerWith(Maria, MariaJobs());

            var response = await Recommend(Client(user));
            var body = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "data", "detailed", "hasPreferences", "page", "query", "skillCount", "status" }, Names(body));
            Assert.False(body.GetProperty("detailed").GetBoolean());

            foreach (var job in Jobs(body))
            {
                var match = job.GetProperty("joblink_match");

                Assert.Equal(new[] { "band", "detailed", "score" }, Names(match));
                Assert.Equal(new[] { "label", "level" }, Names(match.GetProperty("band")));
                Assert.False(match.GetProperty("detailed").GetBoolean());
            }
        }

        [Fact]
        public async Task A_free_response_contains_no_sub_score_note_or_matched_skill_anywhere_in_a_match()
        {
            var user = SeekerWith(Maria, MariaJobs());

            var body = await Read(await Recommend(Client(user)));

            foreach (var job in Jobs(body))
            {
                var match = job.GetProperty("joblink_match").GetRawText();

                foreach (var leak in new[] { "skills", "location", "salary", "matched", "note", "total", "preferred area", "Mentions", "React", "SQL" })
                    Assert.DoesNotContain(leak, match, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task The_jobs_keep_every_field_the_search_gave_them()
        {
            var user = SeekerWith(Maria, MariaJobs());

            var body = await Read(await Recommend(Client(user)));

            var first = Jobs(body).Single(j => j.GetProperty("joblink_job_id").GetInt32() == 1);

            Assert.Equal("React SQL Developer", first.GetProperty("job_title").GetString());
            Assert.Equal("Company 1", first.GetProperty("employer_name").GetString());
            Assert.Equal("External", first.GetProperty("joblink_source").GetString());
            Assert.Equal("https://www.linkedin.com/jobs/view/1", first.GetProperty("job_apply_link").GetString());
        }

        // ----- what a Premium plan is sent ---------------------------------------------------------------------------

        [Fact]
        public async Task A_premium_job_seeker_also_gets_how_each_part_scored_and_the_matched_skills()
        {
            var user = SeekerWith(Maria, MariaJobs());
            MakePremium(user);

            var body = await Read(await Recommend(Client(user)));

            Assert.True(body.GetProperty("detailed").GetBoolean());

            var top = Jobs(body)[0].GetProperty("joblink_match");

            Assert.Equal(new[] { "band", "detailed", "location", "salary", "score", "skills" }, Names(top));
            Assert.True(top.GetProperty("detailed").GetBoolean());
            Assert.Equal(100, top.GetProperty("score").GetInt32());
            Assert.Equal(new[] { "React", "SQL" }, top.GetProperty("skills").GetProperty("matched").EnumerateArray().Select(m => m.GetString()).ToArray());
            Assert.Equal("Mentions 2 of your 2 skills", top.GetProperty("skills").GetProperty("note").GetString());
            Assert.Equal("In your preferred area", top.GetProperty("location").GetProperty("note").GetString());
            Assert.Equal("Meets your salary range", top.GetProperty("salary").GetProperty("note").GetString());

            var analyst = Jobs(body)[1].GetProperty("joblink_match");

            Assert.Equal(JsonValueKind.Null, analyst.GetProperty("salary").GetProperty("score").ValueKind);        // left out of the score...
            Assert.Equal("Salary not listed", analyst.GetProperty("salary").GetProperty("note").GetString());      // ...and says why
            Assert.Equal(0, analyst.GetProperty("location").GetProperty("score").GetInt32());
        }

        [Fact]
        public async Task Free_and_premium_get_the_very_same_scores_and_bands_for_the_same_jobs()
        {
            var free = SeekerWith(Maria, MariaJobs());
            var premium = NewUser();
            Factory.Profiles.Set(premium, Maria);
            MakePremium(premium);

            var freeBody = await Read(await Recommend(Client(free)));
            var premiumBody = await Read(await Recommend(Client(premium)));

            Assert.Equal(new[] { 100, 38, 17 }, Scores(freeBody));
            Assert.Equal(Scores(freeBody), Scores(premiumBody));
            Assert.Equal(
                Jobs(freeBody).Select(j => j.GetProperty("joblink_match").GetProperty("band").GetRawText()),
                Jobs(premiumBody).Select(j => j.GetProperty("joblink_match").GetProperty("band").GetRawText()));
            Assert.Equal(
                Jobs(freeBody).Select(j => j.GetProperty("joblink_job_id").GetInt32()),
                Jobs(premiumBody).Select(j => j.GetProperty("joblink_job_id").GetInt32()));
        }

        // ----- the plan is read from the database, on every request -------------------------------------------------------

        [Fact]
        public async Task Upgrading_shows_the_detail_on_the_same_login_token_and_lapsing_takes_it_away()
        {
            var user = SeekerWith(Maria, MariaJobs());
            var client = Client(user);                                    // one token for the whole test

            Assert.False((await Read(await Recommend(client))).GetProperty("detailed").GetBoolean());

            Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, "/api/Subscription/upgrade", "{\"billing\":\"Monthly\"}")).StatusCode);

            var upgraded = await Read(await Recommend(client));
            Assert.True(upgraded.GetProperty("detailed").GetBoolean());
            Assert.True(Jobs(upgraded)[0].GetProperty("joblink_match").TryGetProperty("skills", out _));

            Factory.Clock.Advance(TimeSpan.FromDays(40));                 // premium_until has passed - nothing ran, the token didn't change

            var lapsed = await Read(await Recommend(client));
            Assert.False(lapsed.GetProperty("detailed").GetBoolean());
            Assert.False(Jobs(lapsed)[0].GetProperty("joblink_match").TryGetProperty("skills", out _));
            Assert.Equal(Scores(upgraded), Scores(lapsed));               // the same numbers, less shown
        }

        [Fact]
        public async Task A_plan_that_cannot_be_read_gets_the_smaller_view_never_the_bigger_one()
        {
            var user = SeekerWith(Maria, MariaJobs());
            MakePremium(user);
            Factory.Subscriptions.Unreadable.Add(user);

            var response = await Recommend(Client(user));
            var body = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(body.GetProperty("detailed").GetBoolean());
            Assert.Equal(new[] { "band", "detailed", "score" }, Names(Jobs(body)[0].GetProperty("joblink_match")));
        }

        // ----- who they are and what they are scored against comes from the server -----------------------------------

        [Fact]
        public async Task Nothing_the_caller_sends_changes_who_they_are_their_skills_or_their_plan()
        {
            var user = SeekerWith(Maria, MariaJobs());
            var other = NewUser();
            Factory.Profiles.Set(other, new ScoringProfile(new[] { "Cobol" }));
            Factory.Profiles.Reads.Clear();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Recommendations?userId={other}&skills=Cobol&plan=Premium&detailed=true&isPremium=true&query=hacked&role=admin")
            {
                Content = JsonBody("{\"userId\":" + other + ",\"skills\":[\"Cobol\"],\"plan\":\"Premium\",\"detailed\":true}")
            };
            request.Headers.Add("X-Plan", "Premium");

            var body = await Read(await Client(user).SendAsync(request));

            Assert.Equal(new[] { user }, Factory.Profiles.Reads.Distinct().ToArray());          // only the token's owner was ever read
            Assert.False(body.GetProperty("detailed").GetBoolean());
            Assert.Equal(new[] { 100, 38, 17 }, Scores(body));                                    // scored as Maria, not as the Cobol programmer
            Assert.Equal("Frontend Developer jobs in Makati", body.GetProperty("query").GetString());
            Assert.Equal("Frontend Developer jobs in Makati", Factory.Search.LastQuery);
        }

        [Fact]
        public async Task Two_job_seekers_are_each_scored_against_their_own_resume()
        {
            var react = SeekerWith(Maria, MariaJobs());
            var cook = NewUser();
            Factory.Profiles.Set(cook, new ScoringProfile(new[] { "Cooking" }, new ScoringPreferences("Manila"), "Chef"));

            var reactScores = Scores(await Read(await Recommend(Client(react))));
            var cookScores = Scores(await Read(await Recommend(Client(cook))));

            Assert.NotEqual(reactScores, cookScores);
            Assert.Equal("Chef jobs in Manila", Factory.Search.LastQuery);
        }

        // ----- the search that is made ---------------------------------------------------------------------------------

        [Fact]
        public async Task The_search_is_built_from_the_resume_role_remote_preference_and_first_place()
        {
            var profile = new ScoringProfile(new[] { "React" }, new ScoringPreferences("Cebu, Davao", "remote"), "QA Engineer");
            var user = SeekerWith(profile);

            var body = await Read(await Recommend(Client(user)));

            Assert.Equal("QA Engineer remote jobs in Cebu", body.GetProperty("query").GetString());
            Assert.Equal("QA Engineer remote jobs in Cebu", Factory.Search.LastQuery);
        }

        [Theory]
        [InlineData("", 1)]
        [InlineData("?page=3", 3)]
        [InlineData("?page=0", 1)]
        [InlineData("?page=-4", 1)]
        [InlineData("?page=99", 10)]
        public async Task The_page_is_kept_between_1_and_10(string query, int expected)
        {
            var user = SeekerWith(Maria);

            var body = await Read(await Recommend(Client(user), query));

            Assert.Equal(expected, body.GetProperty("page").GetInt32());
            Assert.Equal(expected, Factory.Search.Searches[^1].Page);
        }

        [Fact]
        public async Task Without_skills_there_is_nothing_to_match_and_no_search_is_spent()
        {
            var user = NewUser();
            Factory.Profiles.Set(user, new ScoringProfile(Array.Empty<string>(), new ScoringPreferences("Makati")));
            Factory.Search.Returns(MariaJobs());
            var searches = Factory.Search.Searches.Count;

            var response = await Recommend(Client(user));
            var body = await Read(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, body.GetProperty("skillCount").GetInt32());
            Assert.Empty(Jobs(body));
            Assert.Equal(searches, Factory.Search.Searches.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Even_with_nothing_to_match_the_answer_says_which_view_the_plan_gets(bool premium)
        {
            var user = NewUser();

            if (premium)
                MakePremium(user);

            var body = await Read(await Recommend(Client(user)));

            Assert.Equal(premium, body.GetProperty("detailed").GetBoolean());
        }

        [Fact]
        public async Task A_job_seeker_who_never_filled_anything_in_gets_the_same_empty_answer()
        {
            var body = await Read(await Recommend(Client(NewUser())));

            Assert.Equal(0, body.GetProperty("skillCount").GetInt32());
            Assert.False(body.GetProperty("hasPreferences").GetBoolean());
            Assert.Empty(Jobs(body));
        }

        [Theory]
        [InlineData(null, null, 0, 0, false)]
        [InlineData("Makati", null, 0, 0, true)]
        [InlineData(null, "hybrid", 0, 0, true)]
        [InlineData(null, null, 30000, 0, true)]
        [InlineData(null, null, 0, 60000, true)]
        public async Task hasPreferences_says_whether_any_preference_is_saved(string? place, string? arrangement, double min, double max, bool expected)
        {
            var user = SeekerWith(new ScoringProfile(new[] { "React" }, new ScoringPreferences(place, arrangement, min, max)));

            Assert.Equal(expected, (await Read(await Recommend(Client(user)))).GetProperty("hasPreferences").GetBoolean());
        }

        // ----- order -------------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Jobs_come_best_match_first()
        {
            var user = SeekerWith(Maria, MariaJobs());

            var body = await Read(await Recommend(Client(user)));

            Assert.Equal(new[] { 1, 2, 3 }, Jobs(body).Select(j => j.GetProperty("joblink_job_id").GetInt32()));
            Assert.Equal(new[] { 100, 38, 17 }, Scores(body));
        }

        [Fact]
        public async Task Equal_scores_are_ordered_by_how_many_skills_the_job_mentions_then_by_the_searchs_order()
        {
            // Six skills: mentioning 5 or 6 of them is a full 100 either way.
            var skills = new[] { "aa", "bb", "cc", "dd", "ee", "ff" };
            var profile = new ScoringProfile(skills);
            var user = SeekerWith(profile,
                Job(10, "five", "aa bb cc dd ee"),
                Job(11, "six first", "aa bb cc dd ee ff"),
                Job(12, "six second", "ff ee dd cc bb aa"),
                Job(13, "one", "aa"));

            var body = await Read(await Recommend(Client(user)));

            Assert.Equal(new[] { 11, 12, 10, 13 }, Jobs(body).Select(j => j.GetProperty("joblink_job_id").GetInt32()));
            Assert.Equal(new[] { 100, 100, 100, 20 }, Scores(body));
        }

        // ----- the job feed failing ----------------------------------------------------------------------------------------

        [Theory]
        [InlineData(429, "The monthly job search limit has been reached. Please try again later.")]
        [InlineData(502, "Could not reach the job search service.")]
        [InlineData(503, "The JSearch API key is not configured on the server.")]
        [InlineData(504, "The job search service took too long to respond.")]
        public async Task A_failed_search_is_reported_with_its_status_and_message(int status, string message)
        {
            var user = NewUser();
            Factory.Profiles.Set(user, Maria);
            Factory.Search.Fails(status, message);

            var response = await Recommend(Client(user));
            var body = await Read(response);

            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(message, body.GetProperty("message").GetString());
            Assert.False(body.TryGetProperty("data", out _));
        }

        [Fact]
        public async Task Entries_that_are_not_jobs_are_skipped()
        {
            var user = SeekerWith(Maria, MariaJobs()[1], "not a job", 42, null!);

            var body = await Read(await Recommend(Client(user)));

            Assert.Single(Jobs(body));
        }

        [Fact]
        public async Task Asking_twice_gives_the_same_answer_and_never_stacks_a_match_on_a_cached_job()
        {
            var user = SeekerWith(Maria, MariaJobs());
            var client = Client(user);

            var first = await (await Recommend(client)).Content.ReadAsStringAsync();
            var second = await (await Recommend(client)).Content.ReadAsStringAsync();

            Assert.Equal(first, second);
            Assert.Equal(3, JsonDocument.Parse(second).RootElement.GetProperty("data").GetArrayLength());
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(second, "joblink_match").Count);
        }
    }
}
