using JobLinkv2.Models;
using JobLinkv2.Services.Resumes.Export;
using Xunit;

namespace Joblink.Tests
{
    // ResumeExportMapper.Build: turning the app's own rows into the JSON JobLink-AI expects
    // (JobLink-AI/app/models.py: ResumeData). Pure and offline - no database, no HTTP.
    public class ResumeExportMapperTests
    {
        private static UserModel User(string fullName = "Maria Santos", string email = "maria@example.com") =>
            new() { UserId = 1, FullName = fullName, Email = email, Role = "user" };

        private static ExperienceModel Experience(string? description = null, DateTime? start = null, DateTime? end = null) => new()
        {
            ExperienceId = 1, ResumeId = 1, CompanyName = "Acme", Position = "Developer",
            Description = description, StartDate = start, EndDate = end
        };

        [Fact]
        public void The_four_known_templates_are_exactly_what_JobLink_AI_registers()
        {
            Assert.Equal(new[] { "harvard", "reverse_chronological", "functional", "ats" }, ResumeExportMapper.TemplateIds);
        }

        [Theory]
        [InlineData("harvard", true)]
        [InlineData("reverse_chronological", true)]
        [InlineData("functional", true)]
        [InlineData("ats", true)]
        [InlineData("Harvard", false)]     // case matters - the caller (ResumeController) lower-cases first
        [InlineData("standard", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsKnownTemplate(string? template, bool expected)
        {
            Assert.Equal(expected, ResumeExportMapper.IsKnownTemplate(template));
        }

        [Fact]
        public void Name_and_email_come_from_the_account_not_from_the_profile()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard");

            Assert.Equal("Maria Santos", dto.PersonalInfo.FullName);
            Assert.Equal("maria@example.com", dto.PersonalInfo.Email);
        }

        [Fact]
        public void The_profile_maps_phone_address_and_the_two_links()
        {
            var profile = new ProfileModel { Phone = "0917 000 0000", Address = "Makati", LinkedinUrl = "linkedin.com/in/maria", GithubUrl = "github.com/maria" };

            var dto = ResumeExportMapper.Build(User(), profile, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard");

            Assert.Equal("0917 000 0000", dto.PersonalInfo.Phone);
            Assert.Equal("Makati", dto.PersonalInfo.Location);
            Assert.Equal("linkedin.com/in/maria", dto.PersonalInfo.Linkedin);
            // No dedicated "portfolio" column exists - the profile's GitHub link is the closest fit.
            Assert.Equal("github.com/maria", dto.PersonalInfo.Portfolio);
        }

        [Fact]
        public void No_profile_at_all_leaves_every_optional_contact_field_null_not_empty_string()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard");

            Assert.Null(dto.PersonalInfo.Phone);
            Assert.Null(dto.PersonalInfo.Location);
            Assert.Null(dto.PersonalInfo.Linkedin);
            Assert.Null(dto.PersonalInfo.Portfolio);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_profile_fields_are_sent_as_null_not_as_blank_strings(string? blank)
        {
            var profile = new ProfileModel { Phone = blank, Address = blank, LinkedinUrl = blank, GithubUrl = blank };

            var dto = ResumeExportMapper.Build(User(), profile, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard");

            Assert.Null(dto.PersonalInfo.Phone);
            Assert.Null(dto.PersonalInfo.Location);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_summary_is_null_a_real_one_is_trimmed(string? blank)
        {
            Assert.Null(ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), blank, "harvard").CareerInfo.ProfessionalSummary);

            var withSummary = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), "  Built things.  ", "harvard");
            Assert.Equal("Built things.", withSummary.CareerInfo.ProfessionalSummary);
        }

        [Fact]
        public void The_requested_template_is_set_on_the_resume_verbatim()
        {
            Assert.Equal("ats", ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "ats").Template);
        }

        // ----- skills ---------------------------------------------------------------------------------

        [Fact]
        public void Every_skill_goes_under_Technical_Skills_theres_no_category_to_read_from()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), new[] { "React", "Leadership", "SQL" }, null, "harvard");

            Assert.Equal(new[] { "React", "Leadership", "SQL" }, dto.Skills.TechnicalSkills);
            Assert.Empty(dto.Skills.SoftSkills);
            Assert.Empty(dto.Skills.ProgrammingLanguages);
            Assert.Empty(dto.Skills.Tools);
            Assert.Empty(dto.Skills.Technologies);
        }

        [Fact]
        public void Blank_skill_names_are_dropped()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), new[] { "React", "", "   ", "SQL" }, null, "harvard");

            Assert.Equal(new[] { "React", "SQL" }, dto.Skills.TechnicalSkills);
        }

        // ----- education --------------------------------------------------------------------------------

        [Fact]
        public void Education_maps_school_degree_and_years()
        {
            var edu = new EducationModel { EducationId = 1, ResumeId = 1, SchoolName = "UP", Degree = "BS Computer Science", StartDate = new DateTime(2018, 6, 1), EndDate = new DateTime(2022, 4, 1) };

            var dto = ResumeExportMapper.Build(User(), null, new[] { edu }, Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard").Education.Single();

            Assert.Equal("UP", dto.School);
            Assert.Equal("BS Computer Science", dto.Degree);
            Assert.Equal("2018", dto.StartYear);
            Assert.Equal("2022", dto.GraduationYear);
        }

        [Fact]
        public void A_school_name_that_is_missing_is_an_empty_string_not_null_JobLink_AI_requires_it()
        {
            var edu = new EducationModel { EducationId = 1, ResumeId = 1, SchoolName = null };

            Assert.Equal("", ResumeExportMapper.Build(User(), null, new[] { edu }, Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard").Education.Single().School);
        }

        [Fact]
        public void Education_with_no_dates_has_no_years()
        {
            var edu = new EducationModel { EducationId = 1, ResumeId = 1, SchoolName = "UP" };

            var dto = ResumeExportMapper.Build(User(), null, new[] { edu }, Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard").Education.Single();

            Assert.Null(dto.StartYear);
            Assert.Null(dto.GraduationYear);
        }

        [Fact]
        public void Education_entries_keep_their_order()
        {
            var first = new EducationModel { EducationId = 1, ResumeId = 1, SchoolName = "First" };
            var second = new EducationModel { EducationId = 2, ResumeId = 1, SchoolName = "Second" };

            var dto = ResumeExportMapper.Build(User(), null, new[] { first, second }, Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard");

            Assert.Equal(new[] { "First", "Second" }, dto.Education.Select(e => e.School));
        }

        // ----- experience ---------------------------------------------------------------------------------

        [Fact]
        public void Company_and_position_map_directly()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience() }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Equal("Acme", dto.Company);
            Assert.Equal("Developer", dto.Position);
        }

        [Fact]
        public void Dates_are_formatted_as_month_and_year()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience(start: new DateTime(2022, 1, 15), end: new DateTime(2023, 6, 1)) }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Equal("Jan 2022", dto.StartDate);
            Assert.Equal("Jun 2023", dto.EndDate);
        }

        [Fact]
        public void A_start_date_with_no_end_date_is_read_as_still_current()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience(start: new DateTime(2022, 1, 1), end: null) }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Equal("Jan 2022", dto.StartDate);
            Assert.Equal("Present", dto.EndDate);
        }

        [Fact]
        public void With_neither_date_theres_no_start_and_no_made_up_Present()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience(start: null, end: null) }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Null(dto.StartDate);
            Assert.Null(dto.EndDate);
        }

        [Fact]
        public void A_one_line_description_is_the_paragraph_not_a_single_item_bullet_list()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience(description: "Built dashboards.") }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Equal("Built dashboards.", dto.Description);
            Assert.Empty(dto.Responsibilities);
        }

        [Fact]
        public void A_multi_line_description_becomes_one_bullet_per_line_and_no_paragraph()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience(description: "Built dashboards.\nWorked with SQL.\nMentored two interns.") }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Null(dto.Description);
            Assert.Equal(new[] { "Built dashboards.", "Worked with SQL.", "Mentored two interns." }, dto.Responsibilities);
        }

        [Fact]
        public void Blank_lines_between_real_lines_are_dropped_and_each_line_is_trimmed()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience(description: "  First line.  \n\n   \nSecond line.\r\n\r\nThird line.") }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Equal(new[] { "First line.", "Second line.", "Third line." }, dto.Responsibilities);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void No_real_description_text_is_null_with_no_bullets(string? blank)
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { Experience(description: blank) }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Null(dto.Description);
            Assert.Empty(dto.Responsibilities);
        }

        [Fact]
        public void A_missing_company_or_position_is_an_empty_string_not_null()
        {
            var exp = new ExperienceModel { ExperienceId = 1, ResumeId = 1, CompanyName = null, Position = null };

            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), new[] { exp }, Array.Empty<string>(), null, "harvard").Experience.Single();

            Assert.Equal("", dto.Company);
            Assert.Equal("", dto.Position);
        }

        // ----- what is never collected yet ----------------------------------------------------------------

        [Fact]
        public void Certifications_projects_and_top_level_achievements_are_always_empty()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), null, "harvard");

            Assert.Empty(dto.Certifications);
            Assert.Empty(dto.Projects);
            Assert.Empty(dto.Achievements);
        }

        [Fact]
        public void Target_position_and_career_objective_have_no_source_column_and_stay_null()
        {
            var dto = ResumeExportMapper.Build(User(), null, Array.Empty<EducationModel>(), Array.Empty<ExperienceModel>(), Array.Empty<string>(), "a summary", "harvard");

            Assert.Null(dto.CareerInfo.TargetPosition);
            Assert.Null(dto.CareerInfo.CareerObjective);
        }
    }
}
