namespace JobLinkv2.Services.Matching
{
    // The job search that finds a job seeker's recommendations, built from their resume and preferences.
    public static class RecommendationQuery
    {
        // The job search endpoint refuses anything longer.
        public const int MaxLength = 200;

        // "<latest job title, or top 3 skills>[ remote] jobs in <first preferred place, or Philippines>"
        public static string Build(ScoringProfile profile)
        {
            var preferences = profile.Preferences ?? new ScoringPreferences();

            var place = JsCompat.Trim((preferences.PreferredLocation ?? "").Split(',')[0]);

            if (place.Length == 0)
                place = "Philippines";

            var focus = profile.Role.Length > 0 ? profile.Role : string.Join(" ", profile.Skills.Take(3));

            var remote = preferences.WorkArrangement == "remote" ? " remote" : "";

            var query = $"{focus}{remote} jobs in {place}";

            return query.Length <= MaxLength ? query : JsCompat.Trim(query[..MaxLength]);
        }

        // Their current job (no end date), otherwise the most recently ended one; the later start wins a
        // tie, then the order they were entered. Entries without a title are ignored.
        public static string LatestRole(IEnumerable<(string? Position, DateTime? Start, DateTime? End)> experience)
        {
            var epoch = new DateTime(1970, 1, 1);

            var latest = experience
                .Where(item => JsCompat.Trim(item.Position ?? "").Length > 0)
                .OrderByDescending(item => item.End ?? DateTime.MaxValue)
                .ThenByDescending(item => item.Start ?? epoch)
                .FirstOrDefault();

            return latest.Position is null ? "" : JsCompat.Trim(latest.Position);
        }
    }
}
