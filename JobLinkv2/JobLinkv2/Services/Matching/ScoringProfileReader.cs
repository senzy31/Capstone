using System.Globalization;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Resumes;

namespace JobLinkv2.Services.Matching
{
    // Reads what the score needs to know about a job seeker - always from the database, always by the id of
    // the person asking, never from the request. Recommendations use it for the caller; an employer ranking
    // applicants can use it for each applicant.
    public interface IScoringProfileReader
    {
        ScoringProfile Read(int userId);
    }

    // The dashboard used to assemble this from four API calls; this is the same reading, in the same order:
    //   skills      the skill names on their FIRST resume (lowest resume id), trimmed, blanks dropped,
    //               the same spelling counted once, in skill-id order
    //   role        their latest job title (see RecommendationQuery.LatestRole)
    //   preferences their saved job preferences, if any
    public sealed class SqlScoringProfileReader : IScoringProfileReader
    {
        private readonly ResumeDataStore _resumes;
        private readonly SkillStore _skills;

        public SqlScoringProfileReader(ResumeDataStore resumes, SkillStore skills)
        {
            _resumes = resumes;
            _skills = skills;
        }

        public ScoringProfile Read(int userId)
        {
            var saved = _resumes.GetPreference(userId);

            var preferences = saved is null
                ? null
                : new ScoringPreferences(saved.PreferredLocation, saved.WorkArrangement, ToDouble(saved.MinSalary), ToDouble(saved.MaxSalary));

            var resume = _resumes.ListResumes(userId).FirstOrDefault();

            if (resume is null)
                return new ScoringProfile(Array.Empty<string>(), preferences);

            var names = _skills.List().ToDictionary(skill => skill.SkillId, skill => skill.SkillName);

            var skills = (_resumes.ListResumeSkills(userId, resume.ResumeId) ?? Array.Empty<Models.ResumeSkillsModel>())
                .Select(link => names.TryGetValue(link.SkillId, out var name) ? JsCompat.Trim(name ?? "") : "")
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var role = RecommendationQuery.LatestRole(
                (_resumes.ListExperience(userId, resume.ResumeId) ?? Array.Empty<Models.ExperienceModel>())
                    .Select(entry => (entry.Position, entry.StartDate, entry.EndDate)));

            return new ScoringProfile(skills, preferences, role);
        }

        // Through text, so the double is exactly the number a JSON reader would have read: 30000.50 -> 30000.5.
        private static double ToDouble(decimal? value) =>
            value is { } number ? double.Parse(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : 0;
    }
}
