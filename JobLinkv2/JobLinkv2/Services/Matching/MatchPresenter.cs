namespace JobLinkv2.Services.Matching
{
    public sealed record BandView(string Level, string Label);

    // What a FREE plan is sent: the overall score and its band, and nothing else. There is no property
    // here for a sub-score or a matched skill, so no code path can put one in the response by mistake.
    public sealed record MatchSummary(bool Detailed, int Score, BandView Band);

    // What a PREMIUM plan is sent: the same score and band, plus how each part scored and why.
    public sealed record MatchDetail(bool Detailed, int Score, BandView Band, SkillsView Skills, PartView Location, PartView Salary);

    // A part with nothing to compare has no Score (it was left out of the overall score) and a Note that says why.
    public sealed record SkillsView(int? Score, IReadOnlyList<string> Matched, int Total, string Note);

    public sealed record PartView(int? Score, string Note);

    // Decides how much of a score a plan gets to see. The score itself is never touched - it is worked out
    // once, the same for everyone; this only chooses what to hand over.
    public static class MatchPresenter
    {
        public static object For(SuitabilityResult result, ScoringJob job, ScoringProfile profile, bool detailed) =>
            detailed ? Detail(result, job, profile) : Summary(result);

        public static MatchSummary Summary(SuitabilityResult result) =>
            new(false, result.Score, new BandView(result.Band.Level, result.Band.Label));

        public static MatchDetail Detail(SuitabilityResult result, ScoringJob job, ScoringProfile profile)
        {
            var preferences = profile.Preferences ?? new ScoringPreferences();

            var skills = result.Skills is { } s
                ? new SkillsView(s.Score, s.Matched, s.Total, DescribeSkills(s))
                : new SkillsView(null, Array.Empty<string>(), 0, "No skills on your resume yet");

            var location = result.Location is { } l
                ? new PartView(l.Score, l.Note)
                : new PartView(null, "No location preference set");

            var salary = result.Salary is { } m
                ? new PartView(m.Score, m.Note)
                : new PartView(null, DescribeMissingSalary(job, preferences));

            return new MatchDetail(true, result.Score, new BandView(result.Band.Level, result.Band.Label), skills, location, salary);
        }

        private static string DescribeSkills(SkillsPart skills)
        {
            var noun = skills.Total == 1 ? "skill" : "skills";

            return skills.Matched.Count > 0
                ? $"Mentions {skills.Matched.Count} of your {skills.Total} {noun}"
                : $"Mentions none of your {skills.Total} {noun}";
        }

        // Why the salary part was left out.
        private static string DescribeMissingSalary(ScoringJob job, ScoringPreferences preferences)
        {
            if (preferences.MinSalary == 0 && preferences.MaxSalary == 0)
                return "No salary preference set";

            if (job.MinSalary == 0 && job.MaxSalary == 0)
                return "Salary not listed";

            return "Listed in a currency we can't compare";
        }
    }
}
