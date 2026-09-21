using JobLinkv2.Services.Matching;
using Xunit;

namespace Joblink.Tests
{
    // The job search a job seeker's recommendations come from, and how their latest job title is chosen
    // (both moved from the dashboard's JavaScript, unchanged).
    public class RecommendationQueryTests
    {
        private static ScoringProfile Profile(string role, string[] skills, ScoringPreferences? preferences = null) => new(skills, preferences, role);

        // ----- the search text -------------------------------------------------------------------------------

        [Fact]
        public void The_query_is_the_latest_role_and_the_first_preferred_place()
        {
            var query = RecommendationQuery.Build(Profile("Frontend Developer", new[] { "React" }, new ScoringPreferences("Makati, Cebu")));

            Assert.Equal("Frontend Developer jobs in Makati", query);
        }

        [Fact]
        public void Without_a_role_the_top_three_skills_are_used()
        {
            var query = RecommendationQuery.Build(Profile("", new[] { "React", "SQL", "Node.js", "Docker" }));

            Assert.Equal("React SQL Node.js jobs in Philippines", query);
        }

        [Fact]
        public void Someone_who_wants_remote_work_gets_remote_added()
        {
            var query = RecommendationQuery.Build(Profile("QA Engineer", new[] { "Selenium" }, new ScoringPreferences("Cebu", "remote")));

            Assert.Equal("QA Engineer remote jobs in Cebu", query);
        }

        [Theory]
        [InlineData("hybrid")]
        [InlineData("onsite")]
        [InlineData(null)]
        [InlineData("")]
        public void Only_remote_changes_the_query(string? arrangement)
        {
            Assert.Equal("QA Engineer jobs in Cebu", RecommendationQuery.Build(Profile("QA Engineer", new[] { "x" }, new ScoringPreferences("Cebu", arrangement))));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(" , Cebu")]
        public void A_missing_or_blank_first_place_means_the_whole_country(string? location)
        {
            Assert.EndsWith("jobs in Philippines", RecommendationQuery.Build(Profile("Chef", new[] { "x" }, new ScoringPreferences(location))));
        }

        [Fact]
        public void The_first_place_is_trimmed()
        {
            Assert.Equal("Chef jobs in Quezon City", RecommendationQuery.Build(Profile("Chef", new[] { "x" }, new ScoringPreferences("  Quezon City  , Cebu"))));
        }

        [Fact]
        public void No_preferences_at_all_is_fine()
        {
            Assert.Equal("Chef jobs in Philippines", RecommendationQuery.Build(Profile("Chef", new[] { "x" })));
        }

        [Fact]
        public void A_query_too_long_for_the_job_search_is_cut_to_fit()
        {
            var query = RecommendationQuery.Build(Profile(new string('a', 150), new[] { "x" }, new ScoringPreferences(new string('b', 200))));

            Assert.True(query.Length <= RecommendationQuery.MaxLength);
            Assert.StartsWith(new string('a', 150), query);
        }

        // ----- the latest role ---------------------------------------------------------------------------------------

        private static readonly DateTime Jan2020 = new(2020, 1, 1);
        private static readonly DateTime Jun2020 = new(2020, 6, 1);
        private static readonly DateTime Jan2021 = new(2021, 1, 1);

        [Fact]
        public void A_job_with_no_end_date_is_the_current_one()
        {
            var role = RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[]
            {
                ("Intern", Jan2020, Jun2020),
                ("Frontend Developer", Jan2021, null),
                ("Cashier", Jan2020, new DateTime(2019, 1, 1)),
            });

            Assert.Equal("Frontend Developer", role);
        }

        [Fact]
        public void Otherwise_the_one_that_ended_most_recently()
        {
            var role = RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[]
            {
                ("Intern", Jan2020, Jun2020),
                ("Analyst", Jan2020, Jan2021),
                ("Clerk", Jan2020, new DateTime(2019, 1, 1)),
            });

            Assert.Equal("Analyst", role);
        }

        [Fact]
        public void With_the_same_end_the_later_start_wins()
        {
            var role = RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[]
            {
                ("Old", Jan2020, Jan2021),
                ("New", Jun2020, Jan2021),
            });

            Assert.Equal("New", role);
        }

        [Fact]
        public void With_several_current_jobs_the_later_start_wins_then_the_first_entered()
        {
            Assert.Equal("B", RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[] { ("A", Jan2020, null), ("B", Jun2020, null) }));
            Assert.Equal("A", RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[] { ("A", Jan2020, null), ("B", Jan2020, null) }));
        }

        [Fact]
        public void A_job_with_no_start_date_ranks_below_one_that_has_one()
        {
            Assert.Equal("Dated", RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[] { ("Undated", null, null), ("Dated", Jan2020, null) }));
        }

        [Fact]
        public void Entries_without_a_title_are_ignored_and_the_title_is_trimmed()
        {
            var role = RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[]
            {
                (null, Jan2021, null),
                ("   ", Jan2021, null),
                ("  Barista ", Jan2020, Jun2020),
            });

            Assert.Equal("Barista", role);
        }

        [Fact]
        public void No_experience_means_no_role()
        {
            Assert.Equal("", RecommendationQuery.LatestRole(Array.Empty<(string?, DateTime?, DateTime?)>()));
            Assert.Equal("", RecommendationQuery.LatestRole(new (string?, DateTime?, DateTime?)[] { (null, null, null) }));
        }
    }
}
