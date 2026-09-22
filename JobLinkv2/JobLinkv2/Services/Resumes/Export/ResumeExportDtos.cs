namespace JobLinkv2.Services.Resumes.Export
{
    // Mirrors JobLink-AI's Pydantic ResumeData exactly (JobLink-AI/app/models.py), so the JSON
    // POSTed to that service's /api/resume/generate-pdf and /generate-docx is exactly what it
    // expects. Serialized with JsonNamingPolicy.SnakeCaseLower (see PythonResumeService), so a
    // property here is ordinary PascalCase and must convert to the exact Python field name -
    // "Linkedin", not "LinkedIn", the same convention JobLinkv2.Models.ProfileModel already uses
    // for LinkedinUrl/GithubUrl.
    public sealed record PersonalInfoDto(
        string FullName,
        string Email,
        string? Phone,
        string? Location,
        string? Linkedin,
        string? Portfolio);

    public sealed record CareerInfoDto(
        string? TargetPosition,
        string? CareerObjective,
        string? ProfessionalSummary);

    public sealed record EducationDto(
        string School,
        string? Degree,
        string? FieldOfStudy,
        string? StartYear,
        string? GraduationYear,
        IReadOnlyList<string> Achievements);

    public sealed record ExperienceDto(
        string Company,
        string Position,
        string? StartDate,
        string? EndDate,
        string? Description,
        IReadOnlyList<string> Responsibilities,
        IReadOnlyList<string> Achievements);

    public sealed record SkillsDto(
        IReadOnlyList<string> TechnicalSkills,
        IReadOnlyList<string> SoftSkills,
        IReadOnlyList<string> ProgrammingLanguages,
        IReadOnlyList<string> Tools,
        IReadOnlyList<string> Technologies);

    // The full resume, as JobLink-AI's ResumeData. Certifications, Projects and the top-level
    // Achievements are typed as plain objects and always sent empty: the app doesn't collect that
    // data anywhere yet (no table, no UI) - Python's templates already skip a section that's empty,
    // so nothing renders wrong, there's just nothing to show yet.
    public sealed record ResumeDataDto(
        PersonalInfoDto PersonalInfo,
        CareerInfoDto CareerInfo,
        IReadOnlyList<EducationDto> Education,
        IReadOnlyList<ExperienceDto> Experience,
        SkillsDto Skills,
        IReadOnlyList<object> Certifications,
        IReadOnlyList<object> Projects,
        IReadOnlyList<object> Achievements,
        string Template);

    public sealed record DocumentGenerationRequestDto(ResumeDataDto Resume);
}
