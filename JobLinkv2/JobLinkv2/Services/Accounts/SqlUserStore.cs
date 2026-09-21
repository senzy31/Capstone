using Dapper;
using JobLinkv2.Models;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.Accounts
{
    public sealed class SqlUserStore : IUserStore
    {
        // SQL Server error numbers for a unique index / constraint violation.
        private const int UniqueIndexViolation = 2601;
        private const int UniqueConstraintViolation = 2627;

        private const string Columns = @"
            user_id AS UserId, full_name AS FullName, email AS Email, password_hash AS PasswordHash,
            role AS Role, company_name AS CompanyName, created_at AS CreatedAt,
            ISNULL(is_deleted, 0) AS IsDeleted";

        private readonly string _connectionString;

        public SqlUserStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        // varchar columns: send ANSI parameters so SQL Server can use the indexes.
        private static DbString Ansi(string? value, int length) =>
            new() { Value = value, IsAnsi = true, Length = length };

        private static bool IsUniqueViolation(SqlException ex) =>
            ex.Number is UniqueIndexViolation or UniqueConstraintViolation;

        public UserModel? FindById(int userId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<UserModel>(
                $"SELECT {Columns} FROM Users WHERE user_id = @userId AND is_deleted = 0",
                new { userId });
        }

        public UserModel? FindByEmail(string email)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<UserModel>(
                $"SELECT TOP 1 {Columns} FROM Users WHERE email = @email AND is_deleted = 0",
                new { email = Ansi(email, 100) });
        }

        public int CreateUser(NewUser user)
        {
            try
            {
                using var db = Open();

                return db.ExecuteScalar<int>(@"
                    INSERT INTO Users (full_name, email, password_hash, role, company_name, created_at, is_deleted)
                    VALUES (@FullName, @Email, @PasswordHash, @Role, @CompanyName, @CreatedAt, 0);
                    SELECT CAST(SCOPE_IDENTITY() AS int);",
                    new
                    {
                        user.FullName,
                        Email = Ansi(user.Email, 100),
                        user.PasswordHash,
                        Role = Ansi(user.Role, 50),
                        user.CompanyName,
                        user.CreatedAt
                    });
            }
            catch (SqlException ex) when (IsUniqueViolation(ex))
            {
                throw new DuplicateEmailException();
            }
        }

        public bool UpdateAccount(int userId, string fullName, string email, string? companyName)
        {
            try
            {
                using var db = Open();

                return db.Execute(@"
                    UPDATE Users
                       SET full_name = @fullName, email = @email, company_name = @companyName
                     WHERE user_id = @userId AND is_deleted = 0",
                    new { userId, fullName, email = Ansi(email, 100), companyName }) == 1;
            }
            catch (SqlException ex) when (IsUniqueViolation(ex))
            {
                throw new DuplicateEmailException();
            }
        }

        public bool SetPasswordHash(int userId, string passwordHash)
        {
            using var db = Open();

            return db.Execute(
                "UPDATE Users SET password_hash = @passwordHash WHERE user_id = @userId AND is_deleted = 0",
                new { userId, passwordHash }) == 1;
        }
    }
}
