using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobLinkv2.Services.Resumes;
using static Joblink.Services.Resumes.RequestText;

namespace Joblink.Services.Resumes
{
    // What a client may send for its own profile, resumes and everything on them.
    // These classes are the allow-list: anything else in the JSON body (a user id,
    // an owner, is_deleted...) is ignored. Who the caller is comes from the login
    // token, and which rows they may touch is decided in ResumeDataStore's SQL.
    // Limits match the database columns.

    public sealed class SaveProfileRequest
    {
        // Optional. A user has one profile, so this only has to agree with it.
        public int? ProfileId { get; set; }

        [StringLength(20)] public string? Phone { get; set; }
        [StringLength(1000)] public string? Address { get; set; }
        [StringLength(255), WebUrl] public string? LinkedinUrl { get; set; }
        [StringLength(255), WebUrl] public string? GithubUrl { get; set; }

        public ProfileFields ToFields() => new(Clean(Phone), Clean(Address), Clean(LinkedinUrl), Clean(GithubUrl));
    }

    public sealed class CreateResumeRequest
    {
        [StringLength(100)] public string? Title { get; set; }
        [StringLength(50)] public string? TemplateType { get; set; }
    }

    public sealed class UpdateResumeRequest
    {
        [Required] public int? ResumeId { get; set; }

        [StringLength(100)] public string? Title { get; set; }
        [StringLength(50)] public string? TemplateType { get; set; }
        [StringLength(20000)] public string? AiGeneratedContent { get; set; }
    }

    // One class for adding (ResumeId says which of your resumes) and saving
    // (EducationId says which entry); the one that isn't needed is ignored.
    public sealed class SaveEducationRequest
    {
        public int? EducationId { get; set; }
        public int? ResumeId { get; set; }

        [StringLength(100)] public string? SchoolName { get; set; }
        [StringLength(100)] public string? Degree { get; set; }

        [JsonConverter(typeof(MonthOrDateConverter))] public DateTime? StartDate { get; set; }
        [JsonConverter(typeof(MonthOrDateConverter))] public DateTime? EndDate { get; set; }

        public EducationFields ToFields() => new(Clean(SchoolName), Clean(Degree), StartDate, EndDate);
    }

    public sealed class SaveExperienceRequest
    {
        public int? ExperienceId { get; set; }
        public int? ResumeId { get; set; }

        [StringLength(100)] public string? CompanyName { get; set; }
        [StringLength(100)] public string? Position { get; set; }
        [StringLength(20000)] public string? Description { get; set; }

        [JsonConverter(typeof(MonthOrDateConverter))] public DateTime? StartDate { get; set; }
        [JsonConverter(typeof(MonthOrDateConverter))] public DateTime? EndDate { get; set; }

        public ExperienceFields ToFields() => new(Clean(CompanyName), Clean(Position), Clean(Description), StartDate, EndDate);
    }

    public sealed class AddResumeSkillRequest
    {
        [Required] public int? ResumeId { get; set; }
        [Required] public int? SkillId { get; set; }
    }

    // Job preferences keep their own checks (and messages) in the controller.
    public sealed class SaveJobPreferenceRequest
    {
        public string? PreferredLocation { get; set; }
        public string? WorkArrangement { get; set; }
        public decimal? MinSalary { get; set; }
        public decimal? MaxSalary { get; set; }
    }

    // A blank box is no value.
    internal static class RequestText
    {
        public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // Empty, or a web address a browser will open. Not "javascript:" or "data:".
    public sealed class WebUrlAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text))
                return ValidationResult.Success;

            var ok = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                     && string.IsNullOrEmpty(uri.UserInfo);

            return ok
                ? ValidationResult.Success
                : new ValidationResult($"{validationContext.DisplayName} must be a web address starting with http:// or https://.");
        }
    }

    // The Resume Builder's month picker sends "2024-05"; a date picker or the
    // API's own answers use "2024-05-31" or "2024-05-31T00:00:00". All of them
    // are accepted (the columns hold dates, so the time of day is dropped).
    public sealed class MonthOrDateConverter : JsonConverter<DateTime?>
    {
        private static readonly string[] Formats =
        {
            "yyyy-MM", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
        };

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("A date has to be text like 2024-05.");

            var text = reader.GetString();

            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (DateTime.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                return date.Date;

            throw new JsonException($"'{text}' isn't a date. Use a form like 2024-05 or 2024-05-31.");
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value.Value);
        }
    }
}
