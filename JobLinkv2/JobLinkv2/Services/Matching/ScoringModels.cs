using System.Text.Json;

namespace JobLinkv2.Services.Matching
{
    // What the suitability score needs to know about a job. Plain data: no HTTP, no database, so the same
    // scorer can rank a JSearch listing for a job seeker today and an employer's applicants tomorrow.
    // A salary of 0 means "not given" (see JsCompat.NumberOrZero).
    public sealed record ScoringJob(
        string? Title = null,
        string? Description = null,
        string? City = null,
        string? State = null,
        string? Country = null,
        string? Location = null,
        bool IsRemote = false,
        double MinSalary = 0,
        double MaxSalary = 0,
        string? SalaryPeriod = null,
        string? SalaryCurrency = null)
    {
        // A job as JSearch (and the search endpoint) returns it. Fields are read the way the browser
        // read them: text fields must be strings (anything else counts as empty), is-remote only counts
        // when it is exactly true, salaries go through Number(x) || 0.
        public static ScoringJob FromJson(JsonElement job)
        {
            if (job.ValueKind != JsonValueKind.Object)
                return new ScoringJob();

            return new ScoringJob(
                Title: Text(job, "job_title"),
                Description: Text(job, "job_description"),
                City: Text(job, "job_city"),
                State: Text(job, "job_state"),
                Country: Text(job, "job_country"),
                Location: Text(job, "job_location"),
                IsRemote: job.TryGetProperty("job_is_remote", out var remote) && remote.ValueKind == JsonValueKind.True,
                MinSalary: Number(job, "job_min_salary"),
                MaxSalary: Number(job, "job_max_salary"),
                SalaryPeriod: Truthy(job, "job_salary_period"),
                SalaryCurrency: Truthy(job, "job_salary_currency"));
        }

        private static string? Text(JsonElement job, string name) =>
            job.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static double Number(JsonElement job, string name) =>
            job.TryGetProperty(name, out var value) ? JsCompat.NumberOrZero(JsCompat.ToNumber(value)) : 0;

        // `x || fallback`: a non-empty string is itself; a true or non-zero number is its text (which
        // then simply isn't a period or currency we know); anything else is "not there".
        private static string? Truthy(JsonElement job, string name)
        {
            if (!job.TryGetProperty(name, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrEmpty(value.GetString()) ? null : value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.Number => JsCompat.NumberOrZero(value.GetDouble()) == 0 ? null : value.GetRawText(),
                _ => null
            };
        }
    }

    // A job seeker's saved preferences. Salaries are monthly pesos; 0 means "not set".
    public sealed record ScoringPreferences(
        string? PreferredLocation = null,
        string? WorkArrangement = null,
        double MinSalary = 0,
        double MaxSalary = 0);

    // What is known about the job seeker. Skills are already cleaned (trimmed, no blanks, no exact
    // duplicates); Role is their latest job title (used to build the job search, not to score).
    public sealed record ScoringProfile(
        IReadOnlyList<string> Skills,
        ScoringPreferences? Preferences = null,
        string Role = "");

    public sealed record SuitabilityBand(string Level, string Label);

    // Skills: how many of the job seeker's skills the job mentions, and which.
    public sealed record SkillsPart(int Score, IReadOnlyList<string> Matched, int Total);

    // Location or salary: a score and the sentence that explains it.
    public sealed record ScorePart(int Score, string Note);

    // The full answer. A part that had nothing to compare is null and was left out of the score.
    public sealed record SuitabilityResult(int Score, SuitabilityBand Band, SkillsPart? Skills, ScorePart? Location, ScorePart? Salary);
}
