using JobLinkv2.Models;
using JobLinkv2.Services.Accounts;

namespace Joblink.Tests.Support
{
    // An in-memory IUserStore that follows the same rules as SqlUserStore:
    // soft-deleted users are hidden from reads, email is unique
    // case-insensitively (SQL_Latin1_General_CP1_CI_AS) and stays reserved
    // after a soft delete, and every write only touches the columns it names.
    public sealed class InMemoryUserStore : IUserStore
    {
        // The stored rows themselves, so tests can check what really changed.
        public List<UserModel> Users { get; } = new();

        // Every password hash the service saved through SetPasswordHash.
        public List<(int UserId, string Hash)> SavedPasswordHashes { get; } = new();

        private int _nextId = 1;

        // ----- test helpers -----------------------------------------------------

        public UserModel AddUser(
            string email,
            string password = "Sup3rSecret!",
            string role = "user",
            string fullName = "Test User",
            string? companyName = null,
            bool isDeleted = false,
            bool legacyPlainTextPassword = false)
        {
            var user = new UserModel
            {
                UserId = _nextId++,
                FullName = fullName,
                Email = email,
                PasswordHash = legacyPlainTextPassword
                    ? password
                    : BCrypt.Net.BCrypt.HashPassword(password, 4),
                Role = role,
                CompanyName = companyName,
                CreatedAt = new DateTime(2026, 1, 1),
                IsDeleted = isDeleted
            };

            Users.Add(user);

            return user;
        }

        // ----- IUserStore -------------------------------------------------------

        // Reads hand out copies, like a database would.
        private static UserModel Copy(UserModel u) => new()
        {
            UserId = u.UserId, FullName = u.FullName, Email = u.Email, PasswordHash = u.PasswordHash,
            Role = u.Role, CompanyName = u.CompanyName, CreatedAt = u.CreatedAt, IsDeleted = u.IsDeleted
        };

        public UserModel? FindById(int userId) =>
            Users.Where(u => u.UserId == userId && !u.IsDeleted).Select(Copy).FirstOrDefault();

        public UserModel? FindByEmail(string email) =>
            Users.Where(u => Same(u.Email, email) && !u.IsDeleted).Select(Copy).FirstOrDefault();

        public int CreateUser(NewUser user)
        {
            if (Users.Any(u => Same(u.Email, user.Email)))
                throw new DuplicateEmailException();

            return AddRow(user).UserId;
        }

        public bool UpdateAccount(int userId, string fullName, string email, string? companyName)
        {
            var row = Users.FirstOrDefault(u => u.UserId == userId && !u.IsDeleted);

            if (row is null)
                return false;

            if (Users.Any(u => u.UserId != userId && Same(u.Email, email)))
                throw new DuplicateEmailException();

            row.FullName = fullName;
            row.Email = email;
            row.CompanyName = companyName;

            return true;
        }

        public bool SetPasswordHash(int userId, string passwordHash)
        {
            var row = Users.FirstOrDefault(u => u.UserId == userId && !u.IsDeleted);

            if (row is null)
                return false;

            row.PasswordHash = passwordHash;
            SavedPasswordHashes.Add((userId, passwordHash));

            return true;
        }

        private UserModel AddRow(NewUser user)
        {
            var row = new UserModel
            {
                UserId = _nextId++,
                FullName = user.FullName,
                Email = user.Email,
                PasswordHash = user.PasswordHash,
                Role = user.Role,
                CompanyName = user.CompanyName,
                CreatedAt = user.CreatedAt,
                IsDeleted = false
            };

            Users.Add(row);

            return row;
        }

        private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
