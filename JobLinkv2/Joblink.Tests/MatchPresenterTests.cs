using System.Text.Json;
using JobLinkv2.Services.Matching;
using Xunit;

namespace Joblink.Tests
{
    // What a plan is allowed to see of a score. The important half is what a FREE plan is NOT sent.
    public class MatchPresenterTests
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private static readonly ScoringProfile Profile = new(
            new[] { "React", "SQL", "Node.js", "Docker", "AWS", "Git", "Python" },
            new ScoringPreferences("Makati, Cebu", "onsite", 30000, 60000));

        private static readonly ScoringJob Job = new("Full Stack Developer", "React, SQL and Docker every day.", "Makati", "Metro Manila", "PH",
            MinSalary: 40000, MaxSalary: 50000, SalaryPeriod: "MONTH");

        private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), Web);

        private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // ----- Free -----------------------------------------------------------------------------------------

        [Fact]
        public void A_free_plan_gets_the_overall_score_and_band_and_nothing_else()
        {
            var result = SuitabilityScorer.Score(Job, Profile);

            var json = Json(MatchPresenter.For(result, Job, Profile, detailed: false));

            Assert.Equal(new[] { "band", "detailed", "score" }, Names(json));
            Assert.Equal(new[] { "label", "level" }, Names(json.GetProperty("band")));
            Assert.Equal(76, json.GetProperty("score").GetInt32());
            Assert.Equal("Excellent match", json.GetProperty("band").GetProperty("label").GetString());
            Assert.False(json.GetProperty("detailed").GetBoolean());
        }

        [Fact]
        public void The_free_response_has_no_sub_score_note_or_skill_anywhere_in_its_text()
        {
            var result = SuitabilityScorer.Score(Job, Profile);

            var text = JsonSerializer.Serialize(MatchPresenter.For(result, Job, Profile, detailed: false), typeof(MatchSummary), Web);

            foreach (var leak in new[] { "skills", "location", "salary", "matched", "note", "React", "SQL", "Docker", "preferred area", "salary range", "Mentions", "total" })
                Assert.DoesNotContain(leak, text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_free_type_cannot_carry_a_sub_score_because_it_has_no_property_for_one()
        {
            Assert.Equal(new[] { "Band", "Detailed", "Score" }, typeof(MatchSummary).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Equal(new[] { "Label", "Level" }, typeof(BandView).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        // ----- Premium ----------------------------------------------------------------------------------------

        [Fact]
        public void A_premium_plan_gets_the_same_score_plus_how_each_part_scored()
        {
            var result = SuitabilityScorer.Score(Job, Profile);

            var json = Json(MatchPresenter.For(result, Job, Profile, detailed: true));

            Assert.Equal(new[] { "band", "detailed", "location", "salary", "score", "skills" }, Names(json));
            Assert.True(json.GetProperty("detailed").GetBoolean());
            Assert.Equal(76, json.GetProperty("score").GetInt32());

            var skills = json.GetProperty("skills");
            Assert.Equal(new[] { "matched", "note", "score", "total" }, Names(skills));
            Assert.Equal(60, skills.GetProperty("score").GetInt32());
            Assert.Equal(new[] { "React", "SQL", "Docker" }, skills.GetProperty("matched").EnumerateArray().Select(m => m.GetString()).ToArray());
            Assert.Equal(7, skills.GetProperty("total").GetInt32());
            Assert.Equal("Mentions 3 of your 7 skills", skills.GetProperty("note").GetString());

            Assert.Equal(100, json.GetProperty("location").GetProperty("score").GetInt32());
            Assert.Equal("In your preferred area · On-site as you prefer", json.GetProperty("location").GetProperty("note").GetString());
            Assert.Equal(100, json.GetProperty("salary").GetProperty("score").GetInt32());
            Assert.Equal("Meets your salary range", json.GetProperty("salary").GetProperty("note").GetString());
        }

        [Fact]
        public void Free_and_premium_are_given_the_very_same_score_and_band()
        {
            var result = SuitabilityScorer.Score(Job, Profile);

            var free = MatchPresenter.Summary(result);
            var premium = MatchPresenter.Detail(result, Job, Profile);

            Assert.Equal(free.Score, premium.Score);
            Assert.Equal(free.Band, premium.Band);
        }

        [Fact]
        public void Skill_notes_use_the_singular_for_one_skill()
        {
            var profile = new ScoringProfile(new[] { "Cobol" });
            var job = new ScoringJob("Chef");

            var detail = MatchPresenter.Detail(SuitabilityScorer.Score(job, profile), job, profile);

            Assert.Equal(1, detail.Skills.Total);
            Assert.Equal("Mentions none of your 1 skill", detail.Skills.Note);
        }

        [Fact]
        public void Parts_left_out_of_the_score_say_why_and_have_no_score()
        {
            var noPrefs = new ScoringProfile(new[] { "React" });
            var job = new ScoringJob("React Developer");

            var detail = MatchPresenter.Detail(SuitabilityScorer.Score(job, noPrefs), job, noPrefs);

            Assert.Null(detail.Location.Score);
            Assert.Equal("No location preference set", detail.Location.Note);
            Assert.Null(detail.Salary.Score);
            Assert.Equal("No salary preference set", detail.Salary.Note);
        }

        [Theory]
        [InlineData(0, 0, "PH", null, "Salary not listed")]
        [InlineData(40000, 50000, "US", null, "Listed in a currency we can't compare")]
        [InlineData(40000, 50000, "PH", "DECADE", "Listed in a currency we can't compare")]
        public void A_salary_that_cant_be_scored_says_which_reason(double min, double max, string country, string? period, string expected)
        {
            var profile = new ScoringProfile(new[] { "React" }, new ScoringPreferences(MinSalary: 30000));
            var job = new ScoringJob("React Developer", Country: country, MinSalary: min, MaxSalary: max, SalaryPeriod: period);

            var detail = MatchPresenter.Detail(SuitabilityScorer.Score(job, profile), job, profile);

            Assert.Null(detail.Salary.Score);
            Assert.Equal(expected, detail.Salary.Note);
        }

        [Fact]
        public void Without_skills_a_premium_view_still_builds()
        {
            var profile = new ScoringProfile(Array.Empty<string>());
            var job = new ScoringJob("Chef");

            var detail = MatchPresenter.Detail(SuitabilityScorer.Score(job, profile), job, profile);

            Assert.Null(detail.Skills.Score);
            Assert.Empty(detail.Skills.Matched);
        }
    }
}
