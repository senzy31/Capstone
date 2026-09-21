using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using JobLinkv2.Models;
using JobLinkv2.Services.Accounts;

namespace Joblink.Services.Accounts
{
    public static class AccountRoles
    {
        public const string JobSeeker = "user";
        public const string Employer = "employer";
    }

    public enum AccountOutcome
    {
        Ok,
        Invalid,
        NotFound,
        Duplicate,
        Unauthorized,      // wrong email or password at login
        PasswordRequired,  // changing the email needs the current password
        WrongPassword      // ...and it wasn't right
    }

    public sealed record AccountResult<T>(AccountOutcome Outcome, T? Value = default, string? Message = null)
    {
        public bool IsOk => Outcome == AccountOutcome.Ok;
    }

    // Sign up, log in, and the user's own account details. Every write goes
    // through IUserStore's column-specific methods, so nothing a client sends
    // can reach role, the password hash, is_deleted - or a plan added later.
    public sealed class UserAccountService
    {
        public const int MinPasswordLength = 8;
        public const int MaxPasswordBytes = 72;   // bcrypt ignores everything past 72 bytes
        public const int MaxNameLength = 100;
        public const int MaxEmailLength = 100;
        public const int MaxCompanyLength = 255;

        private const string BadLogin = "Invalid email or password";

        private readonly IUserStore _users;
        private readonly TimeProvider _time;
        private readonly int _workFactor;

        // Checked when the email doesn't exist, so "no such account" takes as
        // long as "wrong password" and the two can't be told apart by timing.
        private readonly string _dummyHash;

        public UserAccountService(IUserStore users, TimeProvider time, int bcryptWorkFactor = 11)
        {
            _users = users;
            _time = time;
            _workFactor = bcryptWorkFactor;
            _dummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), bcryptWorkFactor);
        }

        // ----- sign up ----------------------------------------------------------

        public AccountResult<int> Signup(SignupRequest? request)
        {
            if (request is null)
                return Fail<int>(AccountOutcome.Invalid, "Sign-up details are required.");

            var fullName = request.FullName?.Trim();
            var email = request.Email?.Trim();
            var password = request.Password;

            if (string.IsNullOrEmpty(fullName))
                return Fail<int>(AccountOutcome.Invalid, "Please enter your full name.");

            if (fullName.Length > MaxNameLength)
                return Fail<int>(AccountOutcome.Invalid, $"Full name can be at most {MaxNameLength} characters.");

            if (!IsValidEmail(email))
                return Fail<int>(AccountOutcome.Invalid, "Please enter a valid email address.");

            if (string.IsNullOrEmpty(password))
                return Fail<int>(AccountOutcome.Invalid, "Password is required");

            if (password.Length < MinPasswordLength)
                return Fail<int>(AccountOutcome.Invalid, $"Password must be at least {MinPasswordLength} characters.");

            if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
                return Fail<int>(AccountOutcome.Invalid, $"Password is too long (at most {MaxPasswordBytes} bytes).");

            // Only two kinds of account can be created through the API.
            var role = string.IsNullOrWhiteSpace(request.Role)
                ? AccountRoles.JobSeeker
                : request.Role.Trim().ToLowerInvariant();

            if (role != AccountRoles.JobSeeker && role != AccountRoles.Employer)
                return Fail<int>(AccountOutcome.Invalid, "Account type must be a job seeker or an employer.");

            string? company = null;

            if (role == AccountRoles.Employer)
            {
                company = request.CompanyName?.Trim();

                if (string.IsNullOrEmpty(company))
                    return Fail<int>(AccountOutcome.Invalid, "Please enter your company name.");

                if (company.Length > MaxCompanyLength)
                    return Fail<int>(AccountOutcome.Invalid, $"Company name can be at most {MaxCompanyLength} characters.");
            }

            // The password is hashed here and never stored as sent.
            var hash = BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

            try
            {
                var id = _users.CreateUser(new NewUser(
                    fullName, email!, hash, role, company, _time.GetLocalNow().DateTime));

                return new(AccountOutcome.Ok, id);
            }
            catch (DuplicateEmailException)
            {
                return Fail<int>(AccountOutcome.Duplicate, "An account with this email already exists.");
            }
        }

        // ----- log in -----------------------------------------------------------

        public AccountResult<UserModel> Login(string? email, string? password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
                return Fail<UserModel>(AccountOutcome.Unauthorized, BadLogin);

            var user = _users.FindByEmail(email.Trim());

            if (user is null)
            {
                BCrypt.Net.BCrypt.Verify(password, _dummyHash);
                return Fail<UserModel>(AccountOutcome.Unauthorized, BadLogin);
            }

            return PasswordMatches(user, password)
                ? new(AccountOutcome.Ok, user)
                : Fail<UserModel>(AccountOutcome.Unauthorized, BadLogin);
        }

        // ----- your own account -------------------------------------------------

        public UserModel? Get(int userId) => _users.FindById(userId);

        public AccountResult<UserModel> UpdateAccount(int userId, UpdateAccountRequest? request)
        {
            if (request is null)
                return Fail<UserModel>(AccountOutcome.Invalid, "Account details are required.");

            var user = _users.FindById(userId);

            if (user is null)
                return Fail<UserModel>(AccountOutcome.NotFound, "Account not found.");

            // A field that isn't sent stays as it is.
            var fullName = request.FullName is null ? user.FullName : request.FullName.Trim();
            var email = request.Email is null ? user.Email : request.Email.Trim();

            if (string.IsNullOrEmpty(fullName))
                return Fail<UserModel>(AccountOutcome.Invalid, "Please enter your full name.");

            if (fullName.Length > MaxNameLength)
                return Fail<UserModel>(AccountOutcome.Invalid, $"Full name can be at most {MaxNameLength} characters.");

            if (!IsValidEmail(email))
                return Fail<UserModel>(AccountOutcome.Invalid, "Please enter a valid email address.");

            var company = user.CompanyName;

            if (user.Role == AccountRoles.Employer && request.CompanyName is not null)
            {
                company = request.CompanyName.Trim();

                if (company.Length == 0)
                    return Fail<UserModel>(AccountOutcome.Invalid, "Please enter your company name.");

                if (company.Length > MaxCompanyLength)
                    return Fail<UserModel>(AccountOutcome.Invalid, $"Company name can be at most {MaxCompanyLength} characters.");
            }

            // The email is what you log in with, so changing it (not just its
            // capitals) needs the current password. Otherwise anyone holding a
            // stolen login token could lock the owner out for good.
            if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(request.CurrentPassword))
                    return Fail<UserModel>(AccountOutcome.PasswordRequired, "Enter your current password to change your email.");

                if (!PasswordMatches(user, request.CurrentPassword))
                    return Fail<UserModel>(AccountOutcome.WrongPassword, "That password isn't correct.");
            }

            try
            {
                if (!_users.UpdateAccount(userId, fullName, email!, company))
                    return Fail<UserModel>(AccountOutcome.NotFound, "Account not found.");
            }
            catch (DuplicateEmailException)
            {
                return Fail<UserModel>(AccountOutcome.Duplicate, "That email is already in use.");
            }

            var updated = _users.FindById(userId);

            return updated is null
                ? Fail<UserModel>(AccountOutcome.NotFound, "Account not found.")
                : new(AccountOutcome.Ok, updated);
        }

        // ----- helpers ----------------------------------------------------------

        // Accounts created before bcrypt was introduced still hold a plain-text
        // password_hash (not a valid bcrypt hash, so Verify throws
        // SaltParseException). For those, compare plainly once and, on a match,
        // hash and save it so it is never stored in plain text again.
        private bool PasswordMatches(UserModel user, string supplied)
        {
            if (string.IsNullOrEmpty(user.PasswordHash))
                return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(supplied, user.PasswordHash);
            }
            catch (SaltParseException)
            {
                var stored = Encoding.UTF8.GetBytes(user.PasswordHash);
                var given = Encoding.UTF8.GetBytes(supplied);

                if (!CryptographicOperations.FixedTimeEquals(stored, given))
                    return false;

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(supplied, _workFactor);
                _users.SetPasswordHash(user.UserId, user.PasswordHash);

                return true;
            }
        }

        private static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrEmpty(email) || email.Length > MaxEmailLength)
                return false;

            // TryCreate accepts "Name <a@b.com>" and comments; the address it
            // parses has to be exactly what was typed.
            return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;
        }

        private static AccountResult<T> Fail<T>(AccountOutcome outcome, string message) =>
            new(outcome, default, message);
    }
}
