using JobLinkv2.Services.Apply;
using Xunit;

namespace Joblink.Tests
{
    // How an employer's applicant list is ordered. The suitability score always decides;
    // Priority Application only ever breaks a tie.
    public class ApplicantRankerTests
    {
        private static readonly DateTime Start = new(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);

        private static RankedApplicant Applicant(int id, int score, bool priority = false, int hoursAfterStart = 0) =>
            new(ApplicationId: id, UserId: 1000 + id, Score: score, IsPriority: priority, AppliedAt: Start.AddHours(hoursAfterStart));

        private static int[] Order(params RankedApplicant[] applicants) =>
            ApplicantRanker.Rank(applicants).Select(a => a.ApplicationId).ToArray();

        [Fact]
        public void A_higher_score_ranks_first()
        {
            Assert.Equal(new[] { 2, 3, 1 }, Order(Applicant(1, 60), Applicant(2, 90), Applicant(3, 75)));
        }

        [Fact]
        public void Priority_never_lifts_an_applicant_over_a_better_score()
        {
            // A Premium applicant one point behind is still behind - even though they are priority.
            var order = Order(Applicant(1, 80, priority: true), Applicant(2, 81));

            Assert.Equal(new[] { 2, 1 }, order);
        }

        [Fact]
        public void Priority_never_lifts_an_applicant_over_a_better_score_however_many_are_priority()
        {
            var order = Order(
                Applicant(1, 50, priority: true),
                Applicant(2, 51, priority: true),
                Applicant(3, 52),
                Applicant(4, 100));

            Assert.Equal(new[] { 4, 3, 2, 1 }, order);
        }

        [Fact]
        public void On_the_same_score_priority_goes_first()
        {
            var order = Order(Applicant(1, 80), Applicant(2, 80, priority: true), Applicant(3, 80));

            Assert.Equal(2, order[0]);
        }

        [Fact]
        public void Priority_wins_a_tie_even_when_the_normal_application_came_first()
        {
            var order = Order(Applicant(1, 70, priority: false, hoursAfterStart: 0), Applicant(2, 70, priority: true, hoursAfterStart: 5));

            Assert.Equal(new[] { 2, 1 }, order);
        }

        [Fact]
        public void Among_equals_the_earlier_application_goes_first()
        {
            var order = Order(
                Applicant(1, 70, priority: true, hoursAfterStart: 6),
                Applicant(2, 70, priority: true, hoursAfterStart: 2),
                Applicant(3, 70, priority: false, hoursAfterStart: 9),
                Applicant(4, 70, priority: false, hoursAfterStart: 1));

            Assert.Equal(new[] { 2, 1, 4, 3 }, order);
        }

        [Fact]
        public void A_full_tie_falls_back_to_the_application_id()
        {
            Assert.Equal(new[] { 1, 2, 3 }, Order(Applicant(3, 70), Applicant(1, 70), Applicant(2, 70)));
        }

        [Fact]
        public void Ranking_never_changes_a_score_or_a_priority_flag()
        {
            var input = new[] { Applicant(1, 61, priority: true), Applicant(2, 64), Applicant(3, 61) };

            var ranked = ApplicantRanker.Rank(input);

            foreach (var original in input)
                Assert.Contains(original, ranked);                  // records compare by value: nothing was altered
        }

        [Fact]
        public void The_order_does_not_depend_on_the_order_they_are_passed_in()
        {
            var applicants = new[]
            {
                Applicant(1, 90, priority: true, hoursAfterStart: 3),
                Applicant(2, 90, hoursAfterStart: 1),
                Applicant(3, 72, priority: true, hoursAfterStart: 0),
                Applicant(4, 72, priority: true, hoursAfterStart: 0),
                Applicant(5, 40),
                Applicant(6, 90, priority: true, hoursAfterStart: 3)
            };

            var expected = ApplicantRanker.Rank(applicants).Select(a => a.ApplicationId).ToArray();
            var random = new Random(20260921);

            for (var round = 0; round < 50; round++)
            {
                var shuffled = applicants.OrderBy(_ => random.Next()).ToArray();

                Assert.Equal(expected, ApplicantRanker.Rank(shuffled).Select(a => a.ApplicationId).ToArray());
            }
        }

        [Fact]
        public void Whatever_the_mix_a_higher_score_is_never_placed_below_a_lower_one()
        {
            var random = new Random(7);
            var applicants = Enumerable.Range(1, 200)
                .Select(id => Applicant(id, random.Next(0, 101), random.Next(2) == 0, random.Next(0, 100)))
                .ToList();

            var ranked = ApplicantRanker.Rank(applicants);

            for (var i = 1; i < ranked.Count; i++)
            {
                Assert.True(ranked[i - 1].Score >= ranked[i].Score, "a lower score was ranked above a higher one");

                // and within one score, every priority applicant comes before every normal one
                if (ranked[i - 1].Score == ranked[i].Score)
                    Assert.False(!ranked[i - 1].IsPriority && ranked[i].IsPriority, "a normal application was ranked above a priority one with the same score");
            }
        }

        [Fact]
        public void An_empty_list_ranks_to_an_empty_list()
        {
            Assert.Empty(ApplicantRanker.Rank(Array.Empty<RankedApplicant>()));
        }
    }
}
