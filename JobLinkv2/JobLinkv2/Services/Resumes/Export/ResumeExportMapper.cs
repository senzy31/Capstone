using System.Globalization;
using JobLinkv2.Models;

namespace JobLinkv2.Services.Resumes.Export
{
    // Builds the JSON JobLink-AI expects (ResumeExportDtos) from the rows the app actually has.
    // Pure and DB-independent: every argument is already-loaded data, nothing here opens a
    // connection. The caller (Joblink/Controllers/ResumeController.Export) is the one that reads
    // the database and checks who owns what.
    public static class ResumeExportMapper
    {
        // The four templates JobLink-AI knows about (JobLink-AI/app/models.py: TemplateType).
        // "ats" is Premium only - the caller checks that, this only validates the name is real.
        public static readonly IReadOnlyList<string> TemplateIds =
            new[] { "harvard", "reverse_chronological", "functional", "ats" };

        public static bool IsKnownTemplate(string? template) =>
            template != null && TemplateIds.Contains(template);

        public static ResumeDataDto Build(
            UserModel user,
            ProfileModel? profile,
            IReadOnlyList<EducationModel> education,
            IReadOnlyList<ExperienceModel> experience,
            IReadOnlyList<string> skillNames,
            string? summary,
            string template)
        {
            return new ResumeDataDto(
                PersonalInfo: new PersonalInfoDto(
                    FullName: user.FullName,
                    Email: user.Email,
                    Phone: Blank(profile?.Phone),
                    Location: Blank(profile?.Address),
                    Linkedin: Blank(profile?.LinkedinUrl),
                    // JobLink-AI has one generic "portfolio" link and the app has no dedicated
                    // portfolio field - github_url is the closest thing on the Profile.
                    Portfolio: Blank(profile?.GithubUrl)),
                CareerInfo: new CareerInfoDto(
                    TargetPosition: null,   // no matching column - the Resume Builder has no "target position" field
                    CareerObjective: null,
                    ProfessionalSummary: Blank(summary)),
                Education: education.Select(MapEducation).ToList(),
                Experience: experience.Select(MapExperience).ToList(),
                Skills: new SkillsDto(
                    // The app's skills are one flat list (Resume_Skills -> Skills), with no category
                    // of their own; JobLink-AI's templates group by category. Everything goes under
                    // one bucket rather than guessing at a classification that isn't there.
                    TechnicalSkills: skillNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList(),
                    SoftSkills: Array.Empty<string>(),
                    ProgrammingLanguages: Array.Empty<string>(),
                    Tools: Array.Empty<string>(),
                    Technologies: Array.Empty<string>()),
                Certifications: Array.Empty<object>(),
                Projects: Array.Empty<object>(),
                Achievements: Array.Empty<object>(),
                Template: template);
        }

        private static EducationDto MapEducation(EducationModel edu) => new(
            School: Blank(edu.SchoolName) ?? "",
            Degree: Blank(edu.Degree),
            FieldOfStudy: null,   // no matching column
            StartYear: Year(edu.StartDate),
            GraduationYear: Year(edu.EndDate),
            Achievements: Array.Empty<string>());

        private static ExperienceDto MapExperience(ExperienceModel exp)
        {
            var (description, responsibilities) = SplitDescription(exp.Description);

            return new ExperienceDto(
                Company: Blank(exp.CompanyName) ?? "",
                Position: Blank(exp.Position) ?? "",
                StartDate: Month(exp.StartDate),
                // No end date but a start date is how the Resume Builder marks a current job
                // (ResumeBuilder.js: isCurrent = !entry.endDate) - "Present" matches what the page
                // itself already shows for the same row.
                EndDate: exp.EndDate.HasValue ? Month(exp.EndDate) : (exp.StartDate.HasValue ? "Present" : null),
                Description: description,
                Responsibilities: responsibilities,
                Achievements: Array.Empty<string>());
        }

        // The Resume Builder's Description field is one free-text box. A single line becomes the
        // paragraph every template shows under the job; several lines are read as one bullet each
        // (what a resume-writer would type there), so JobLink-AI's per-line rendering is used
        // instead of one run-on paragraph.
        private static (string? Description, IReadOnlyList<string> Responsibilities) SplitDescription(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (null, Array.Empty<string>());

            var lines = text.Replace("\r\n", "\n").Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            return lines.Count > 1 ? (null, lines) : (text.Trim(), Array.Empty<string>());
        }

        private static string? Month(DateTime? date) => date?.ToString("MMM yyyy", CultureInfo.InvariantCulture);

        private static string? Year(DateTime? date) => date?.Year.ToString(CultureInfo.InvariantCulture);

        private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
