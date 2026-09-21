using JobLinkv2.Models;

namespace Joblink.Services.Accounts
{
    // What a client may send when it signs up. Anything else in the JSON body
    // (a plan, premium_until, is_deleted, a user id...) is ignored - these
    // classes are the allow-list. Role is limited to "user" or "employer" by
    // UserAccountService; no other role can be created through the API.
    public sealed class SignupRequest
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Role { get; set; }
        public string? CompanyName { get; set; }
    }

    // What a logged-in user may change about their own account. Not here on
    // purpose: role, password, plan / premium_until, is_deleted, created_at and
    // the user id (who you are comes from the login token, never the body).
    // CurrentPassword is only used to confirm an email change.
    public sealed class UpdateAccountRequest
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? CompanyName { get; set; }
        public string? CurrentPassword { get; set; }
    }

    // What the API says about an account. Never includes the password hash.
    public sealed record AccountResponse(
        int UserId,
        string? FullName,
        string? Email,
        string? Role,
        string? CompanyName,
        DateTime CreatedAt)
    {
        public static AccountResponse From(UserModel user) =>
            new(user.UserId, user.FullName, user.Email, user.Role, user.CompanyName, user.CreatedAt);
    }
}
