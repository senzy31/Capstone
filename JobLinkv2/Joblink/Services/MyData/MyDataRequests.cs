using System.ComponentModel.DataAnnotations;

namespace Joblink.Services.MyData
{
    // What a client may send about its own notifications, saved jobs and the shared
    // skills list. These classes are the allow-list: anything else in the JSON body
    // (a user id, the message, is_deleted...) is ignored.

    // Reading a notification is the only change a user can make to one.
    public sealed class UpdateNotificationRequest
    {
        [Required] public int? NotificationId { get; set; }

        public bool IsRead { get; set; } = true;
    }

    public sealed class SaveJobRequest
    {
        [Required] public int? JobId { get; set; }
    }

    public sealed class CreateSkillRequest
    {
        [Required, StringLength(100, MinimumLength = 1), StorableName]
        public string? SkillName { get; set; }
    }

    // The skills column is varchar, so anything outside Latin-1 would silently turn into
    // "?" - and different skills would collapse into one. Refuse those (and control
    // characters) instead of storing something else than what was typed.
    public sealed class StorableNameAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string text)
                return ValidationResult.Success;

            var ok = text.All(c => c <= 0xFF && !char.IsControl(c));

            return ok
                ? ValidationResult.Success
                : new ValidationResult($"{validationContext.DisplayName} can use letters, numbers and common punctuation.");
        }
    }
}
