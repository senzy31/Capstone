using Dapper;
using JobLinkv2.Models;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.Resumes
{
    // What a client may set on each kind of row. Ids, owners, timestamps and the
    // deleted flag are never in here: they are decided by the server.
    public sealed record ProfileFields(string? Phone, string? Address, string? LinkedinUrl, string? GithubUrl);

    public sealed record EducationFields(string? SchoolName, string? Degree, DateTime? StartDate, DateTime? EndDate);

    public sealed record ExperienceFields(string? CompanyName, string? Position, string? Description, DateTime? StartDate, DateTime? EndDate);

    public sealed record PreferenceFields(string? PreferredLocation, string? WorkArrangement, decimal? MinSalary, decimal? MaxSalary);

    public enum AddSkillOutcome
    {
        Added,
        ResumeNotFound,
        SkillNotFound
    }

    // A job seeker's own data: profile, resumes, and what hangs off a resume
    // (education, experience, skills), plus job preferences.
    //
    // Every method takes the caller's user id and puts it in the SQL, so a row
    // that isn't the caller's can't be read, changed or deleted - and can't be
    // reached by asking with a different id. Rows under a resume are found
    // through Resumes.user_id. Soft-deleted rows (is_deleted = 1) are invisible.
    //
    // Nothing here takes a whole model to save: each write names its columns.
    public sealed class ResumeDataStore
    {
        // SQL Server error numbers for a unique index / constraint violation.
        private const int UniqueIndexViolation = 2601;
        private const int UniqueConstraintViolation = 2627;

        private const string ProfileColumns = @"
            profile_id AS ProfileId, user_id AS UserId, phone AS Phone, address AS Address,
            linkedin_url AS LinkedinUrl, github_url AS GithubUrl, ISNULL(is_deleted, 0) AS IsDeleted";

        private const string ResumeColumns = @"
            resume_id AS ResumeId, user_id AS UserId, title AS Title, template_type AS TemplateType,
            ai_generated_content AS AiGeneratedContent, created_at AS CreatedAt, ISNULL(is_deleted, 0) AS IsDeleted";

        private const string EducationColumns = @"
            education_id AS EducationId, resume_id AS ResumeId, school_name AS SchoolName, degree AS Degree,
            start_date AS StartDate, end_date AS EndDate, ISNULL(is_deleted, 0) AS IsDeleted";

        private const string ExperienceColumns = @"
            experience_id AS ExperienceId, resume_id AS ResumeId, company_name AS CompanyName, position AS Position,
            description AS Description, start_date AS StartDate, end_date AS EndDate, ISNULL(is_deleted, 0) AS IsDeleted";

        private const string ResumeSkillColumns = @"
            resume_id AS ResumeId, skill_id AS SkillId, ISNULL(is_deleted, 0) AS IsDeleted";

        private const string PreferenceColumns = @"
            preference_id AS PreferenceId, user_id AS UserId, preferred_location AS PreferredLocation,
            work_arrangement AS WorkArrangement, min_salary AS MinSalary, max_salary AS MaxSalary,
            ISNULL(is_deleted, 0) AS IsDeleted";

        // "resume_id belongs to a live resume of @userId" - the ownership test for
        // everything that hangs off a resume.
        private const string OwnedResumeIds =
            "resume_id IN (SELECT resume_id FROM Resumes WHERE user_id = @userId AND is_deleted = 0)";

        private readonly string _connectionString;

        public ResumeDataStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        // varchar columns: send ANSI parameters so SQL Server can use the indexes
        // and doesn't need an nvarchar -> varchar / text conversion.
        private static DbString Ansi(string? value, int length = 4000) =>
            new() { Value = value, IsAnsi = true, Length = Math.Max(length, value?.Length ?? 0) };

        private static bool ResumeIsYours(SqlConnection db, int userId, int resumeId) =>
            db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Resumes WHERE resume_id = @resumeId AND user_id = @userId AND is_deleted = 0",
                new { resumeId, userId }) == 1;

        // ----- profile ----------------------------------------------------------

        public ProfileModel? GetProfileByUser(int userId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<ProfileModel>(
                $"SELECT TOP 1 {ProfileColumns} FROM Profiles WHERE user_id = @userId AND is_deleted = 0 ORDER BY profile_id",
                new { userId });
        }

        public ProfileModel? GetProfile(int userId, int profileId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<ProfileModel>(
                $"SELECT {ProfileColumns} FROM Profiles WHERE profile_id = @profileId AND user_id = @userId AND is_deleted = 0",
                new { profileId, userId });
        }

        // A user has one profile. Null when they already have one.
        public ProfileModel? AddProfile(int userId, ProfileFields f)
        {
            using var db = Open();

            var id = db.QuerySingle<int?>($@"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {SqlLocks.Take}

                IF EXISTS (SELECT 1 FROM Profiles WHERE user_id = @userId AND is_deleted = 0)
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT CAST(NULL AS int);
                    RETURN;
                END

                INSERT INTO Profiles (user_id, phone, address, linkedin_url, github_url, is_deleted)
                VALUES (@userId, @phone, @address, @linkedinUrl, @githubUrl, 0);

                SELECT CAST(SCOPE_IDENTITY() AS int);
                COMMIT TRANSACTION;",
                new { LockName = SqlLocks.Name("profile", userId), userId, phone = Ansi(f.Phone, 20), address = Ansi(f.Address), linkedinUrl = Ansi(f.LinkedinUrl, 255), githubUrl = Ansi(f.GithubUrl, 255) });

            return id is null ? null : GetProfile(userId, id.Value);
        }

        public bool UpdateProfile(int userId, int profileId, ProfileFields f)
        {
            using var db = Open();

            return db.Execute(@"
                UPDATE Profiles
                   SET phone = @phone, address = @address, linkedin_url = @linkedinUrl, github_url = @githubUrl
                 WHERE profile_id = @profileId AND user_id = @userId AND is_deleted = 0",
                new { profileId, userId, phone = Ansi(f.Phone, 20), address = Ansi(f.Address), linkedinUrl = Ansi(f.LinkedinUrl, 255), githubUrl = Ansi(f.GithubUrl, 255) }) == 1;
        }

        public bool DeleteProfile(int userId, int profileId)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Profiles SET is_deleted = 1 WHERE profile_id = @profileId AND user_id = @userId AND is_deleted = 0",
                new { profileId, userId }) == 1;
        }

        // ----- resumes ----------------------------------------------------------

        public IReadOnlyList<ResumeModel> ListResumes(int userId)
        {
            using var db = Open();

            return db.Query<ResumeModel>(
                $"SELECT {ResumeColumns} FROM Resumes WHERE user_id = @userId AND is_deleted = 0 ORDER BY resume_id",
                new { userId }).ToList();
        }

        public int CountResumes(int userId)
        {
            using var db = Open();

            return db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Resumes WHERE user_id = @userId AND is_deleted = 0", new { userId });
        }

        public ResumeModel? GetResume(int userId, int resumeId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<ResumeModel>(
                $"SELECT {ResumeColumns} FROM Resumes WHERE resume_id = @resumeId AND user_id = @userId AND is_deleted = 0",
                new { resumeId, userId });
        }

        public ResumeModel AddResume(int userId, string? title, string? templateType, DateTime createdAt)
        {
            using var db = Open();

            var id = db.ExecuteScalar<int>(@"
                INSERT INTO Resumes (user_id, title, template_type, created_at, is_deleted)
                VALUES (@userId, @title, @templateType, @createdAt, 0);
                SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { userId, title = Ansi(title, 100), templateType = Ansi(templateType, 50), createdAt });

            return db.QuerySingle<ResumeModel>(
                $"SELECT {ResumeColumns} FROM Resumes WHERE resume_id = @id", new { id });
        }

        // Adds a resume only if the user has fewer than maxLive live ones, deciding and adding
        // as one step (a per-user lock), so parallel requests can't both take the last slot.
        // Null when they are at the limit. Resumes they already have above it are left alone.
        public ResumeModel? TryAddResume(int userId, string? title, string? templateType, DateTime createdAt, int maxLive)
        {
            using var db = Open();

            var id = db.QuerySingle<int?>($@"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {SqlLocks.Take}

                IF (SELECT COUNT(*) FROM Resumes WHERE user_id = @userId AND is_deleted = 0) >= @maxLive
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT CAST(NULL AS int);
                    RETURN;
                END

                INSERT INTO Resumes (user_id, title, template_type, created_at, is_deleted)
                VALUES (@userId, @title, @templateType, @createdAt, 0);

                SELECT CAST(SCOPE_IDENTITY() AS int);
                COMMIT TRANSACTION;",
                new { LockName = SqlLocks.Name("resume", userId), userId, maxLive, title = Ansi(title, 100), templateType = Ansi(templateType, 50), createdAt });

            return id is null
                ? null
                : db.QuerySingle<ResumeModel>($"SELECT {ResumeColumns} FROM Resumes WHERE resume_id = @id", new { id });
        }

        // Title and content are replaced; the template stays unless a new one is sent.
        public bool UpdateResume(int userId, int resumeId, string? title, string? templateType, string? aiGeneratedContent)
        {
            using var db = Open();

            return db.Execute(@"
                UPDATE Resumes
                   SET title = @title,
                       template_type = ISNULL(@templateType, template_type),
                       ai_generated_content = @content
                 WHERE resume_id = @resumeId AND user_id = @userId AND is_deleted = 0",
                new { resumeId, userId, title = Ansi(title, 100), templateType = Ansi(templateType, 50), content = Ansi(aiGeneratedContent) }) == 1;
        }

        public bool DeleteResume(int userId, int resumeId)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Resumes SET is_deleted = 1 WHERE resume_id = @resumeId AND user_id = @userId AND is_deleted = 0",
                new { resumeId, userId }) == 1;
        }

        // ----- education (through a resume you own) -----------------------------

        // Null when the resume isn't yours (or doesn't exist).
        public IReadOnlyList<EducationModel>? ListEducation(int userId, int resumeId)
        {
            using var db = Open();

            if (!ResumeIsYours(db, userId, resumeId))
                return null;

            return db.Query<EducationModel>(
                $"SELECT {EducationColumns} FROM Education WHERE resume_id = @resumeId AND is_deleted = 0 ORDER BY education_id",
                new { resumeId }).ToList();
        }

        public EducationModel? GetEducation(int userId, int educationId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<EducationModel>(
                $"SELECT {EducationColumns} FROM Education WHERE education_id = @educationId AND is_deleted = 0 AND {OwnedResumeIds}",
                new { educationId, userId });
        }

        // Null when the resume isn't yours.
        public EducationModel? AddEducation(int userId, int resumeId, EducationFields f)
        {
            using var db = Open();

            var id = db.QuerySingle<int?>(@"
                INSERT INTO Education (resume_id, school_name, degree, start_date, end_date, is_deleted)
                SELECT @resumeId, @schoolName, @degree, @startDate, @endDate, 0
                 WHERE EXISTS (SELECT 1 FROM Resumes WHERE resume_id = @resumeId AND user_id = @userId AND is_deleted = 0);

                IF @@ROWCOUNT = 1 SELECT CAST(SCOPE_IDENTITY() AS int) ELSE SELECT CAST(NULL AS int);",
                new { resumeId, userId, schoolName = Ansi(f.SchoolName, 100), degree = Ansi(f.Degree, 100), startDate = f.StartDate?.Date, endDate = f.EndDate?.Date });

            return id is null ? null : GetEducation(userId, id.Value);
        }

        public bool UpdateEducation(int userId, int educationId, EducationFields f)
        {
            using var db = Open();

            return db.Execute($@"
                UPDATE Education
                   SET school_name = @schoolName, degree = @degree, start_date = @startDate, end_date = @endDate
                 WHERE education_id = @educationId AND is_deleted = 0 AND {OwnedResumeIds}",
                new { educationId, userId, schoolName = Ansi(f.SchoolName, 100), degree = Ansi(f.Degree, 100), startDate = f.StartDate?.Date, endDate = f.EndDate?.Date }) == 1;
        }

        public bool DeleteEducation(int userId, int educationId)
        {
            using var db = Open();

            return db.Execute(
                $"UPDATE Education SET is_deleted = 1 WHERE education_id = @educationId AND is_deleted = 0 AND {OwnedResumeIds}",
                new { educationId, userId }) == 1;
        }

        // ----- experience (through a resume you own) ----------------------------

        public IReadOnlyList<ExperienceModel>? ListExperience(int userId, int resumeId)
        {
            using var db = Open();

            if (!ResumeIsYours(db, userId, resumeId))
                return null;

            return db.Query<ExperienceModel>(
                $"SELECT {ExperienceColumns} FROM Experience WHERE resume_id = @resumeId AND is_deleted = 0 ORDER BY experience_id",
                new { resumeId }).ToList();
        }

        public ExperienceModel? GetExperience(int userId, int experienceId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<ExperienceModel>(
                $"SELECT {ExperienceColumns} FROM Experience WHERE experience_id = @experienceId AND is_deleted = 0 AND {OwnedResumeIds}",
                new { experienceId, userId });
        }

        public ExperienceModel? AddExperience(int userId, int resumeId, ExperienceFields f)
        {
            using var db = Open();

            var id = db.QuerySingle<int?>(@"
                INSERT INTO Experience (resume_id, company_name, position, description, start_date, end_date, is_deleted)
                SELECT @resumeId, @companyName, @position, @description, @startDate, @endDate, 0
                 WHERE EXISTS (SELECT 1 FROM Resumes WHERE resume_id = @resumeId AND user_id = @userId AND is_deleted = 0);

                IF @@ROWCOUNT = 1 SELECT CAST(SCOPE_IDENTITY() AS int) ELSE SELECT CAST(NULL AS int);",
                new { resumeId, userId, companyName = Ansi(f.CompanyName, 100), position = Ansi(f.Position, 100), description = Ansi(f.Description), startDate = f.StartDate?.Date, endDate = f.EndDate?.Date });

            return id is null ? null : GetExperience(userId, id.Value);
        }

        public bool UpdateExperience(int userId, int experienceId, ExperienceFields f)
        {
            using var db = Open();

            return db.Execute($@"
                UPDATE Experience
                   SET company_name = @companyName, position = @position, description = @description,
                       start_date = @startDate, end_date = @endDate
                 WHERE experience_id = @experienceId AND is_deleted = 0 AND {OwnedResumeIds}",
                new { experienceId, userId, companyName = Ansi(f.CompanyName, 100), position = Ansi(f.Position, 100), description = Ansi(f.Description), startDate = f.StartDate?.Date, endDate = f.EndDate?.Date }) == 1;
        }

        public bool DeleteExperience(int userId, int experienceId)
        {
            using var db = Open();

            return db.Execute(
                $"UPDATE Experience SET is_deleted = 1 WHERE experience_id = @experienceId AND is_deleted = 0 AND {OwnedResumeIds}",
                new { experienceId, userId }) == 1;
        }

        // ----- skills on a resume ----------------------------------------------

        public IReadOnlyList<ResumeSkillsModel>? ListResumeSkills(int userId, int resumeId)
        {
            using var db = Open();

            if (!ResumeIsYours(db, userId, resumeId))
                return null;

            return db.Query<ResumeSkillsModel>(
                $"SELECT {ResumeSkillColumns} FROM Resume_Skills WHERE resume_id = @resumeId AND is_deleted = 0 ORDER BY skill_id",
                new { resumeId }).ToList();
        }

        // Adds the skill to the resume, or brings back one that was removed
        // (a removed link keeps its row - the table's key is resume_id + skill_id).
        // Adding what is already there is fine.
        public AddSkillOutcome AddResumeSkill(int userId, int resumeId, int skillId)
        {
            using var db = Open();

            if (!ResumeIsYours(db, userId, resumeId))
                return AddSkillOutcome.ResumeNotFound;

            if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM Skills WHERE skill_id = @skillId AND is_deleted = 0", new { skillId }) != 1)
                return AddSkillOutcome.SkillNotFound;

            try
            {
                db.Execute(@"
                    UPDATE Resume_Skills SET is_deleted = 0 WHERE resume_id = @resumeId AND skill_id = @skillId;

                    IF @@ROWCOUNT = 0
                        INSERT INTO Resume_Skills (resume_id, skill_id, is_deleted) VALUES (@resumeId, @skillId, 0);",
                    new { resumeId, skillId });
            }
            catch (SqlException ex) when (ex.Number is UniqueIndexViolation or UniqueConstraintViolation)
            {
                // two requests added the same skill at once - it is there either way
            }

            return AddSkillOutcome.Added;
        }

        public bool RemoveResumeSkill(int userId, int resumeId, int skillId)
        {
            using var db = Open();

            return db.Execute(
                $"UPDATE Resume_Skills SET is_deleted = 1 WHERE resume_id = @resumeId AND skill_id = @skillId AND is_deleted = 0 AND {OwnedResumeIds}",
                new { resumeId, skillId, userId }) == 1;
        }

        // ----- job preferences --------------------------------------------------

        public JobPreferenceModel? GetPreference(int userId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<JobPreferenceModel>(
                $"SELECT TOP 1 {PreferenceColumns} FROM Job_Preferences WHERE user_id = @userId AND is_deleted = 0 ORDER BY preference_id",
                new { userId });
        }

        // One row per user: the first save creates it, later saves replace it.
        public JobPreferenceModel SavePreference(int userId, PreferenceFields f)
        {
            using var db = Open();

            db.Execute($@"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {SqlLocks.Take}

                UPDATE Job_Preferences
                   SET preferred_location = @location, work_arrangement = @arrangement,
                       min_salary = @minSalary, max_salary = @maxSalary
                 WHERE user_id = @userId AND is_deleted = 0;

                IF @@ROWCOUNT = 0
                    INSERT INTO Job_Preferences (user_id, preferred_location, work_arrangement, min_salary, max_salary, is_deleted)
                    VALUES (@userId, @location, @arrangement, @minSalary, @maxSalary, 0);

                COMMIT TRANSACTION;",
                new { LockName = SqlLocks.Name("preference", userId), userId, location = Ansi(f.PreferredLocation, 255), arrangement = Ansi(f.WorkArrangement, 20), f.MinSalary, f.MaxSalary });

            return db.QueryFirst<JobPreferenceModel>(
                $"SELECT TOP 1 {PreferenceColumns} FROM Job_Preferences WHERE user_id = @userId AND is_deleted = 0 ORDER BY preference_id",
                new { userId });
        }
    }
}
