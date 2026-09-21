using JobLinkv2.Models;

namespace JobLinkv2.Services.Accounts
{
    // Thrown when an email is already registered (the unique index on
    // Users.email caught it - including emails of soft-deleted accounts).
    public sealed class DuplicateEmailException : Exception
    {
        public DuplicateEmailException() : base("That email is already registered.") { }
    }

    public sealed record NewUser(
        string FullName,
        string Email,
        string PasswordHash,
        string Role,
        string? CompanyName,
        DateTime CreatedAt);

    // Everything the account endpoints need from the Users table. Reads ignore
    // soft-deleted rows.
    //
    // There is deliberately no "save this whole user" method: every write names
    // the columns it changes. A column added later (a plan, premium_until...)
    // therefore can't be written by accident, and can't be written from a request.
    public interface IUserStore
    {
        UserModel? FindById(int userId);

        UserModel? FindByEmail(string email);

        // Returns the new user's id. Throws DuplicateEmailException.
        int CreateUser(NewUser user);

        // Changes only name, email and company. False if there is no such live user.
        // Throws DuplicateEmailException.
        bool UpdateAccount(int userId, string fullName, string email, string? companyName);

        // Changes only the password hash.
        bool SetPasswordHash(int userId, string passwordHash);
    }
}
