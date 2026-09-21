using JobLinkv2.Services.Matching;
using Xunit;

namespace Joblink.Tests
{
    // The rules of docs/scoring.md, one at a time and in words. (SuitabilityGoldenTests proves the numbers
    // match the browser's over 600+ cases; these say what each rule is.)
    public class SuitabilityScorerTests
    {
        private static ScoringProfile Profile(string[] skills, ScoringPreferences? preferences = null) => new(skills, preferences);

        private static ScoringJob Job(string title = "", string description = "", string? city = "Makati", string? state = "Metro Manila", string? country = "PH", bool remote = false,
            double min = 0, double max = 0, string? period = null, string? currency = null) =>
            new(title, description, city, state, country, null, remote, min, max, period, currency);

        private static readonly string[] SevenSkills = { "React", "SQL", "Node.js", "Docker", "AWS", "Git", "Python" };

        private static readonly ScoringPreferences MakatiOnSite = new("Makati, Cebu", "onsite", 30000, 60000);

        // ----- the constants -------------------------------------------------------------------

        [Fact]
        public void The_weights_are_60_20_20_and_the_skill_target_is_5()
        {
            Assert.Equal(60, SuitabilityScorer.SkillsWeight);
            Assert.Equal(20, SuitabilityScorer.LocationWeight);
            Assert.Equal(20, SuitabilityScorer.SalaryWeight);
            Assert.Equal(5, SuitabilityScorer.SkillMatchTarget);
        }

        [Theory]
        [InlineData(100, "excellent", "Excellent match")]
        [InlineData(75, "excellent", "Excellent match")]
        [InlineData(74, "good", "Good match")]
        [InlineData(50, "good", "Good match")]
        [InlineData(49, "fair", "Fair match")]
        [InlineData(25, "fair", "Fair match")]
        [InlineData(24, "low", "Low match")]
        [InlineData(0, "low", "Low match")]
        public void Bands_start_at_75_50_and_25(int score, string level, string label)
        {
            Assert.Equal(new SuitabilityBand(level, label), SuitabilityScorer.BandFor(score));
        }

        // ----- the worked examples in docs/scoring.md ----------------------------------------------

        [Fact]
        public void Example_A_a_strong_match_is_76_Excellent()
        {
            var job = Job("Full Stack Developer", "React, SQL and Docker every day.", min: 40000, max: 50000, period: "MONTH");

            var result = SuitabilityScorer.Score(job, Profile(SevenSkills, MakatiOnSite));

            Assert.Equal(60, result.Skills!.Score);                                         // 3 of target 5
            Assert.Equal(new[] { "React", "SQL", "Docker" }, result.Skills.Matched);
            Assert.Equal(100, result.Location!.Score);
            Assert.Equal("In your preferred area · On-site as you prefer", result.Location.Note);
            Assert.Equal(new ScorePart(100, "Meets your salary range"), result.Salary);
            Assert.Equal(76, result.Score);                                                 // (60*60 + 100*20 + 100*20) / 100
            Assert.Equal("excellent", result.Band.Level);
        }

        [Fact]
        public void Example_B_pay_below_the_minimum_scores_the_shortfall()
        {
            var job = Job("Junior Developer", "React and SQL.", min: 18000, max: 24000, period: "MONTH");

            var result = SuitabilityScorer.Score(job, Profile(SevenSkills, MakatiOnSite));

            Assert.Equal(40, result.Skills!.Score);
            Assert.Equal(new ScorePart(80, "Pays up to ₱24,000/month - under your ₱30,000 minimum"), result.Salary);   // 24,000 / 30,000
            Assert.Equal(60, result.Score);                                                                          // (40*60 + 100*20 + 80*20) / 100
            Assert.Equal("good", result.Band.Level);
        }

        [Fact]
        public void Example_C_a_part_with_nothing_to_compare_is_left_out_and_the_weights_rescale()
        {
            var job = Job("Backend Engineer", "Node.js and AWS. Work from home.", city: "Davao", remote: true);

            var result = SuitabilityScorer.Score(job, Profile(SevenSkills, MakatiOnSite));

            Assert.Equal(new ScorePart(0, "Outside your preferred area · Remote (you prefer on-site)"), result.Location);
            Assert.Null(result.Salary);
            Assert.Equal(30, result.Score);                    // (40*60 + 0*20) / 80
        }

        [Fact]
        public void Example_D_with_no_preferences_the_score_is_the_skills_score()
        {
            var result = SuitabilityScorer.Score(Job("Data Analyst", "SQL and Python reporting.", city: "Manila"), Profile(SevenSkills));

            Assert.Null(result.Location);
            Assert.Null(result.Salary);
            Assert.Equal(40, result.Skills!.Score);
            Assert.Equal(40, result.Score);
        }

        [Fact]
        public void Example_E_yearly_pay_is_converted_to_a_month()
        {
            var job = Job("Developer", "Git.", city: "Cebu", min: 240000, max: 300000, period: "YEAR");

            var result = SuitabilityScorer.Score(job, Profile(new[] { "Git", "Java" }, new ScoringPreferences(MinSalary: 30000)));

            Assert.Equal(50, result.Skills!.Score);
            Assert.Equal(new ScorePart(83, "Pays up to ₱25,000/month - under your ₱30,000 minimum"), result.Salary);    // 300,000 / 12 = 25,000
            Assert.Equal(58, result.Score);                                                                          // 58.25 rounds down
        }

        [Fact]
        public void Example_F_a_dollar_listing_is_never_compared()
        {
            var job = Job("Developer", "React", city: "Austin", country: "US", min: 100000, max: 120000, period: "YEAR");

            var result = SuitabilityScorer.Score(job, Profile(new[] { "React" }, new ScoringPreferences("Austin", null, 30000)));

            Assert.Null(result.Salary);
            Assert.Equal(100, result.Score);
        }

        // ----- skills ---------------------------------------------------------------------------------

        [Theory]
        [InlineData("JavaScript developer", "Java", false)]
        [InlineData("MySQL admin", "SQL", false)]
        [InlineData("sql", "SQL", true)]
        [InlineData("Skills: (SQL), C++/Java.", "C++", true)]
        [InlineData("we use node.js daily", "Node.js", true)]
        [InlineData("Uses C#, C++", "C#", true)]
        [InlineData("react2 sqlite", "React", false)]
        [InlineData("é sql ñ", "SQL", true)]
        public void A_skill_counts_only_as_a_whole_term(string text, string skill, bool mentioned)
        {
            var result = SuitabilityScorer.Score(Job(title: text), Profile(new[] { skill }));

            Assert.Equal(mentioned ? 1 : 0, result.Skills!.Matched.Count);
        }

        [Theory]
        [InlineData(2, 2, 100)]     // both of 2
        [InlineData(5, 10, 100)]    // 5 of 10 is a full match: the target caps at 5
        [InlineData(2, 10, 40)]     // 2 of a target of 5
        [InlineData(1, 3, 33)]      // 1 of a target of 3
        [InlineData(0, 4, 0)]
        [InlineData(7, 7, 100)]     // more than the target is capped at 100
        public void The_skills_score_is_matched_over_min_of_your_skills_and_5(int mentioned, int listed, int expected)
        {
            var skills = Enumerable.Range(0, listed).Select(i => $"skill{(char)('a' + i)}").ToArray();
            var title = string.Join(" ", skills.Take(mentioned));

            Assert.Equal(expected, SuitabilityScorer.Score(Job(title: title), Profile(skills)).Skills!.Score);
        }

        [Fact]
        public void No_skills_means_the_skills_part_is_not_scored()
        {
            var result = SuitabilityScorer.Score(Job(title: "React"), Profile(Array.Empty<string>()));

            Assert.Null(result.Skills);
            Assert.Equal(0, result.Score);            // nothing at all was scored
        }

        [Fact]
        public void The_title_and_the_description_are_both_read()
        {
            Assert.Equal(2, SuitabilityScorer.Score(Job(title: "SQL Developer", description: "Strong React"), Profile(new[] { "SQL", "React" })).Skills!.Matched.Count);
        }

        [Fact]
        public void Matched_skills_keep_the_job_seekers_spelling_and_order()
        {
            var result = SuitabilityScorer.Score(Job(title: "sql react"), Profile(new[] { "React", "SQL", "Go" }));

            Assert.Equal(new[] { "React", "SQL" }, result.Skills!.Matched);
            Assert.Equal(3, result.Skills.Total);
        }

        // ----- location ------------------------------------------------------------------------------------

        [Fact]
        public void A_place_matches_anywhere_inside_the_jobs_place_text()
        {
            var result = SuitabilityScorer.Score(Job(city: "Cebu City", state: null), Profile(Array.Empty<string>(), new ScoringPreferences("cebu")));

            Assert.Equal(new ScorePart(100, "In your preferred area"), result.Location);
        }

        [Fact]
        public void Metro_Manila_preferred_does_not_match_a_job_that_says_only_Manila()
        {
            var result = SuitabilityScorer.Score(Job(city: "Manila", state: null, country: null), Profile(Array.Empty<string>(), new ScoringPreferences("Metro Manila")));

            Assert.Equal(0, result.Location!.Score);
        }

        [Fact]
        public void Several_places_can_be_listed_with_spaces_and_blanks()
        {
            var result = SuitabilityScorer.Score(Job(city: "Quezon City", state: null), Profile(Array.Empty<string>(), new ScoringPreferences(" , CEBU , quezon city ,, ")));

            Assert.Equal(100, result.Location!.Score);
        }

        [Fact]
        public void A_remote_job_satisfies_the_place_only_for_someone_who_asked_for_remote()
        {
            var job = Job(city: "Davao", state: null, remote: true);

            Assert.Equal(100, SuitabilityScorer.Score(job, Profile(Array.Empty<string>(), new ScoringPreferences("Cebu", "remote"))).Location!.Score);   // place ok via remote, and remote = remote
            Assert.Equal(0, SuitabilityScorer.Score(job, Profile(Array.Empty<string>(), new ScoringPreferences("Cebu"))).Location!.Score);
        }

        [Fact]
        public void The_place_and_the_arrangement_are_averaged()
        {
            var result = SuitabilityScorer.Score(Job(), Profile(Array.Empty<string>(), new ScoringPreferences("Makati", "remote")));

            Assert.Equal(new ScorePart(50, "In your preferred area · On-site (you prefer remote)"), result.Location);
        }

        [Theory]
        [InlineData("Work from home allowed", "remote")]
        [InlineData("WFH setup", "remote")]
        [InlineData("There is no remote work.", "remote")]        // a substring test: the word is enough
        [InlineData("hybrid or remote", "remote")]                  // remote wins over hybrid
        [InlineData("a hybrid role", "hybrid")]
        [InlineData("come to the office", "onsite")]
        public void The_work_setup_is_read_from_the_description(string description, string expected)
        {
            Assert.Equal(expected, SuitabilityScorer.WorkSetup(Job(description: description)));
        }

        [Fact]
        public void The_title_does_not_decide_the_work_setup_but_the_remote_flag_does()
        {
            Assert.Equal("onsite", SuitabilityScorer.WorkSetup(Job(title: "Remote Developer")));
            Assert.Equal("remote", SuitabilityScorer.WorkSetup(Job(remote: true)));
        }

        // ----- salary ------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("YEAR", 300000)]
        [InlineData("MONTH", 25000)]
        [InlineData("month", 25000)]
        [InlineData(null, 25000)]        // no period means month
        [InlineData("", 25000)]
        public void Pay_is_converted_to_a_monthly_amount(string? period, double max)
        {
            var result = SuitabilityScorer.Score(Job(max: max, period: period), Profile(Array.Empty<string>(), new ScoringPreferences(MinSalary: 100000)));

            Assert.StartsWith("Pays up to ₱25,000/month", result.Salary!.Note);
        }

        [Theory]
        [InlineData("WEEK", 5000, "Pays up to ₱21,667/month")]       // x 52 / 12
        [InlineData("DAY", 1000, "Pays up to ₱21,667/month")]        // x 5 x 52 / 12
        [InlineData("HOUR", 100, "Pays up to ₱17,333/month")]        // x 40 x 52 / 12
        public void Weekly_daily_and_hourly_pay_use_a_40_hour_5_day_week(string period, double max, string expectedStart)
        {
            var result = SuitabilityScorer.Score(Job(max: max, period: period), Profile(Array.Empty<string>(), new ScoringPreferences(MinSalary: 100000)));

            Assert.StartsWith(expectedStart, result.Salary!.Note);
        }

        [Fact]
        public void Pay_at_or_above_the_minimum_scores_100_however_high()
        {
            Assert.Equal(100, SuitabilityScorer.Score(Job(max: 30000), Profile(Array.Empty<string>(), new ScoringPreferences(MinSalary: 30000))).Salary!.Score);
            Assert.Equal(100, SuitabilityScorer.Score(Job(max: 900000), Profile(Array.Empty<string>(), new ScoringPreferences(MinSalary: 30000, MaxSalary: 60000))).Salary!.Score);
        }

        [Fact]
        public void Open_ended_pay_counts_as_meeting_the_minimum()
        {
            Assert.Equal(100, SuitabilityScorer.Score(Job(min: 5000), Profile(Array.Empty<string>(), new ScoringPreferences(MinSalary: 30000))).Salary!.Score);
        }

        [Fact]
        public void A_preferred_maximum_never_changes_the_score_it_only_makes_the_part_count()
        {
            var job = Job(min: 10000, max: 12000);

            Assert.Equal(100, SuitabilityScorer.Score(job, Profile(Array.Empty<string>(), new ScoringPreferences(MaxSalary: 60000))).Salary!.Score);
            Assert.Null(SuitabilityScorer.Score(job, Profile(Array.Empty<string>(), new ScoringPreferences())).Salary);
        }

        [Theory]
        [InlineData(0, 0, "PH", null, null)]                   // no salary listed
        [InlineData(20000, 25000, "US", null, null)]           // dollars
        [InlineData(20000, 25000, "SG", null, null)]           // a country we have no currency for
        [InlineData(20000, 25000, "PH", "USD", null)]          // the currency field wins over the country
        [InlineData(20000, 25000, "PH", "php", null)]          // and is matched exactly
        [InlineData(20000, 25000, "PH", null, "DECADE")]       // an unknown period
        public void Pay_that_cant_be_compared_leaves_the_salary_part_out(double min, double max, string country, string? currency, string? period)
        {
            var result = SuitabilityScorer.Score(Job(country: country, min: min, max: max, currency: currency, period: period), Profile(Array.Empty<string>(), new ScoringPreferences(MinSalary: 30000)));

            Assert.Null(result.Salary);
        }

        [Fact]
        public void A_php_currency_field_makes_a_listing_comparable_whatever_the_country()
        {
            var result = SuitabilityScorer.Score(Job(country: "US", currency: "PHP", max: 25000), Profile(Array.Empty<string>(), new ScoringPreferences(MinSalary: 30000)));

            Assert.Equal(83, result.Salary!.Score);
        }

        // ----- putting it together ----------------------------------------------------------------------------------------

        [Fact]
        public void A_job_that_mentions_none_of_your_skills_cannot_score_above_40()
        {
            var job = Job(title: "Chef", min: 40000, max: 60000, period: "MONTH");

            var result = SuitabilityScorer.Score(job, Profile(new[] { "Go", "Rust" }, new ScoringPreferences("Makati", "onsite", 30000, 70000)));

            Assert.Equal(0, result.Skills!.Score);
            Assert.Equal(40, result.Score);
            Assert.Equal("fair", result.Band.Level);
        }

        [Fact]
        public void Halves_round_up_not_to_the_nearest_even_number()
        {
            // skills 50 and location 100 -> (50*60 + 100*20) / 80 = 62.5 -> 63 (banker's rounding would say 62)
            var result = SuitabilityScorer.Score(Job(title: "a"), Profile(new[] { "a", "b" }, new ScoringPreferences("Makati")));

            Assert.Equal(63, result.Score);
        }

        [Fact]
        public void The_score_is_the_same_whatever_the_plan_only_the_presentation_differs()
        {
            var job = Job("Full Stack Developer", "React, SQL and Docker every day.", min: 40000, max: 50000, period: "MONTH");
            var profile = Profile(SevenSkills, MakatiOnSite);

            var result = SuitabilityScorer.Score(job, profile);

            Assert.Equal(MatchPresenter.Summary(result).Score, MatchPresenter.Detail(result, job, profile).Score);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(result), System.Text.Json.JsonSerializer.Serialize(SuitabilityScorer.Score(job, profile)));   // asking twice gives the same answer
        }

        // ----- reading a job the way JSearch sends it --------------------------------------------------------------------------

        [Fact]
        public void A_search_result_is_read_field_by_field()
        {
            using var document = System.Text.Json.JsonDocument.Parse(@"{
                ""job_title"": ""React Developer"", ""job_description"": ""We use React."", ""job_city"": ""Makati"", ""job_state"": null, ""job_country"": ""PH"",
                ""job_is_remote"": true, ""job_min_salary"": ""40000"", ""job_max_salary"": 50000, ""job_salary_period"": ""MONTH"", ""job_salary_currency"": """" }");

            var job = ScoringJob.FromJson(document.RootElement);

            Assert.Equal(new ScoringJob("React Developer", "We use React.", "Makati", null, "PH", null, true, 40000, 50000, "MONTH", null), job);
        }

        [Fact]
        public void A_job_that_is_not_an_object_scores_as_an_empty_job()
        {
            using var document = System.Text.Json.JsonDocument.Parse("[1, 2]");

            Assert.Equal(new ScoringJob(), ScoringJob.FromJson(document.RootElement));
        }
    }
}
