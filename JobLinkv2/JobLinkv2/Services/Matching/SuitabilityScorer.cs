using System.Globalization;

namespace JobLinkv2.Services.Matching
{
    // How well a job suits a job seeker, 0-100, from up to three parts: skills (60), location (20) and
    // salary (20). A part with nothing to compare is left out and the others re-weighted.
    //
    // This is the browser's DASHBOARD/Suitability.js moved to the server, rule for rule: docs/scoring.md
    // is the description, tests/golden/suitability.golden.json (produced by the original) is the proof.
    // Change a rule here and both have to change with it.
    //
    // Pure and stateless: the same job and profile always give the same result, whatever the plan -
    // what a plan shows of it is decided elsewhere (MatchPresenter).
    public static class SuitabilityScorer
    {
        public const int SkillsWeight = 60;
        public const int LocationWeight = 20;
        public const int SalaryWeight = 20;

        // Mentioning this many of your skills - or all of them, if you list fewer - is a full skills match.
        public const int SkillMatchTarget = 5;

        private const string Remote = "remote";
        private const string Hybrid = "hybrid";
        private const string OnSite = "onsite";

        public static SuitabilityResult Score(ScoringJob job, ScoringProfile profile)
        {
            var preferences = profile.Preferences ?? new ScoringPreferences();

            var skills = ScoreSkills(job, profile.Skills);
            var location = ScoreLocation(job, preferences);
            var salary = ScoreSalary(job, preferences);

            var weighted = 0;
            var totalWeight = 0;

            if (skills != null) { weighted += skills.Score * SkillsWeight; totalWeight += SkillsWeight; }
            if (location != null) { weighted += location.Score * LocationWeight; totalWeight += LocationWeight; }
            if (salary != null) { weighted += salary.Score * SalaryWeight; totalWeight += SalaryWeight; }

            var score = totalWeight > 0 ? (int)JsCompat.Round((double)weighted / totalWeight) : 0;

            return new SuitabilityResult(score, BandFor(score), skills, location, salary);
        }

        public static SuitabilityBand BandFor(int score)
        {
            if (score >= 75) return new SuitabilityBand("excellent", "Excellent match");
            if (score >= 50) return new SuitabilityBand("good", "Good match");
            if (score >= 25) return new SuitabilityBand("fair", "Fair match");

            return new SuitabilityBand("low", "Low match");
        }

        // ------------------------------------------------------------------
        // Skills
        // ------------------------------------------------------------------

        private static SkillsPart? ScoreSkills(ScoringJob job, IReadOnlyList<string> skills)
        {
            if (skills.Count == 0)
                return null;

            var text = JsCompat.Lower($"{job.Title ?? ""} {job.Description ?? ""}");

            var matched = skills.Where(skill => MentionsSkill(text, JsCompat.Lower(skill))).ToList();

            var target = Math.Min(skills.Count, SkillMatchTarget);

            var score = (int)Math.Min(100, JsCompat.Round((double)matched.Count / target * 100));

            return new SkillsPart(score, matched, skills.Count);
        }

        // A whole-term match: "java" must not match "javascript", "sql" not "mysql". The characters
        // either side must not be an ASCII letter or digit (accents, symbols and spaces are boundaries).
        // Both arguments are already lower-case.
        private static bool MentionsSkill(string text, string skill)
        {
            for (var at = 0; at + skill.Length <= text.Length; at++)
            {
                if (skill.Length > 0)
                {
                    at = text.IndexOf(skill, at, StringComparison.Ordinal);

                    if (at < 0)
                        return false;
                }

                var before = at == 0 || !IsWordChar(text[at - 1]);
                var after = at + skill.Length == text.Length || !IsWordChar(text[at + skill.Length]);

                if (before && after)
                    return true;
            }

            return false;
        }

        private static bool IsWordChar(char ch) => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9');

        // ------------------------------------------------------------------
        // Location and work arrangement
        // ------------------------------------------------------------------

        // What the job's work setup is, worked out from the job (there is no such column): remote if it says
        // so or its description mentions remote / work from home / wfh, else hybrid if the description says
        // so, else on-site. The title is not read.
        public static string WorkSetup(ScoringJob job)
        {
            var description = JsCompat.Lower(job.Description ?? "");

            if (job.IsRemote || description.Contains("remote", StringComparison.Ordinal) ||
                description.Contains("work from home", StringComparison.Ordinal) || description.Contains("wfh", StringComparison.Ordinal))
                return Remote;

            return description.Contains("hybrid", StringComparison.Ordinal) ? Hybrid : OnSite;
        }

        private static string Label(string setup) => setup switch
        {
            Remote => "remote",
            Hybrid => "hybrid",
            OnSite => "on-site",
            _ => setup   // a value the preferences page never saves; the browser said "undefined" here
        };

        private static string Capitalize(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        private static ScorePart? ScoreLocation(ScoringJob job, ScoringPreferences preferences)
        {
            var places = (preferences.PreferredLocation ?? "")
                .Split(',')
                .Select(place => JsCompat.Lower(JsCompat.Trim(place)))
                .Where(place => place.Length > 0)
                .ToList();

            var arrangement = preferences.WorkArrangement ?? "";

            var scores = new List<int>();
            var notes = new List<string>();

            var jobSetup = WorkSetup(job);

            if (places.Count > 0)
            {
                var jobPlace = JsCompat.Lower($"{job.City ?? ""} {job.State ?? ""} {job.Country ?? ""} {job.Location ?? ""}");

                var inPlace = places.Any(place => jobPlace.Contains(place, StringComparison.Ordinal));

                // A remote job can be done from the preferred area too - but only counts if they want remote work.
                var fits = inPlace || (arrangement == Remote && jobSetup == Remote);

                scores.Add(fits ? 100 : 0);
                notes.Add(fits ? "In your preferred area" : "Outside your preferred area");
            }

            if (arrangement.Length > 0)
            {
                var fits = jobSetup == arrangement;

                scores.Add(fits ? 100 : 0);
                notes.Add(fits
                    ? $"{Capitalize(Label(arrangement))} as you prefer"
                    : $"{Capitalize(Label(jobSetup))} (you prefer {Label(arrangement)})");
            }

            if (scores.Count == 0)
                return null;

            return new ScorePart((int)JsCompat.Round(scores.Sum() / (double)scores.Count), string.Join(" · ", notes));
        }

        // ------------------------------------------------------------------
        // Salary
        // ------------------------------------------------------------------

        // Salaries in the app are monthly pesos; listings can be per year, week, day or hour.
        private static double? MonthlyFactor(string period) => period switch
        {
            "YEAR" => 1.0 / 12,
            "MONTH" => 1,
            "WEEK" => 52.0 / 12,
            "DAY" => 5 * 52 / 12.0,
            "HOUR" => 40 * 52 / 12.0,
            _ => null
        };

        private static string Currency(ScoringJob job)
        {
            if (!string.IsNullOrEmpty(job.SalaryCurrency))
                return job.SalaryCurrency;

            return job.Country switch { "PH" => "PHP", "US" => "USD", _ => "" };
        }

        // The job's top monthly pay in pesos, or null when it has no salary or isn't in pesos (so it can't
        // be compared). No stated maximum means open-ended.
        private static double? MonthlyMax(ScoringJob job)
        {
            if (job.MinSalary == 0 && job.MaxSalary == 0)
                return null;

            if (Currency(job) != "PHP")
                return null;

            var period = string.IsNullOrEmpty(job.SalaryPeriod) ? "MONTH" : job.SalaryPeriod.ToUpperInvariant();

            if (MonthlyFactor(period) is not double factor)
                return null;

            return job.MaxSalary != 0 ? job.MaxSalary * factor : double.PositiveInfinity;
        }

        // Pay above what they asked for is never a problem - only pay that falls short of their minimum
        // counts against a job. (Their maximum only decides whether the part is scored at all.)
        private static ScorePart? ScoreSalary(ScoringJob job, ScoringPreferences preferences)
        {
            var wantMin = JsCompat.NumberOrZero(preferences.MinSalary);
            var wantMax = JsCompat.NumberOrZero(preferences.MaxSalary);

            if (wantMin == 0 && wantMax == 0)
                return null;

            if (MonthlyMax(job) is not double payMax)
                return null;

            if (wantMin != 0 && payMax < wantMin)
            {
                return new ScorePart(
                    (int)JsCompat.Round(payMax / wantMin * 100),
                    $"Pays up to {FormatPeso(payMax)}/month - under your {FormatPeso(wantMin)} minimum");
            }

            return new ScorePart(100, "Meets your salary range");
        }

        private static string FormatPeso(double amount) =>
            "₱" + JsCompat.Round(amount).ToString("N0", CultureInfo.InvariantCulture);
    }
}
