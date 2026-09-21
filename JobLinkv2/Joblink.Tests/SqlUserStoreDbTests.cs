using Dapper;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Accounts;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // The real SqlUserStore against the real Joblinkv2 database - what the
    // in-memory fake can't check: the unique email index (and its collation),
    // that soft-deleted users are invisible, and that every write touches
    // only the columns it names. Every test deletes the users it made.
    public sealed class SqlUserStoreDbTests : IDisposable
    {
        private readonly SqlUserStore _store = new(DbConfig.DefaultConnectionString);
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _ids = new();

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private string Email(string label) => $"dbtest.{_tag}.{label}@example.com";

        private NewUser New(string label, string role = "user", string? company = null) =>
            new($"dbtest {_tag}", Email(label), "$2a$04$hashhashhashhashhashhu", role, company, new DateTime(2026, 9, 22, 10, 30, 0));

        private int Create(string label, string role = "user", string? company = null)
        {
            var id = _store.CreateUser(New(label, role, company));
            _ids.Add(id);
            return id;
        }

        private T Value<T>(string sql, object args)
        {
            using var db = Open();
            return db.QuerySingle<T>(sql, args);
        }

        public void Dispose()
        {
            using var db = Open();

            db.Execute("DELETE FROM Users WHERE user_id IN @ids", new { ids = _ids.Count > 0 ? _ids : new List<int> { -1 } });
        }

        [DbFact]
        public void A_created_user_can_be_found_by_id_and_by_email_in_any_case()
        {
            var id = Create("a", company: null);

            var byId = _store.FindById(id)!;
            var byEmail = _store.FindByEmail(Email("a").ToUpperInvariant())!;

            Assert.Equal(id, byId.UserId);
            Assert.Equal(id, byEmail.UserId);
            Assert.Equal("user", byId.Role);
            Assert.False(byId.IsDeleted);
            Assert.Null(byId.CompanyName);
            Assert.Equal(new DateTime(2026, 9, 22, 10, 30, 0), byId.CreatedAt);
        }

        [DbFact]
        public void An_employer_keeps_the_company_name()
        {
            var id = Create("boss", "employer", "Acme Corp");

            Assert.Equal("Acme Corp", _store.FindById(id)!.CompanyName);
        }

        [DbFact]
        public void The_same_email_cannot_be_registered_twice_in_any_case()
        {
            Create("dup");

            Assert.Throws<DuplicateEmailException>(() => _store.CreateUser(New("dup") with { Email = Email("dup").ToUpperInvariant() }));
        }

        [DbFact]
        public void A_soft_deleted_user_is_invisible_but_keeps_the_email_reserved()
        {
            var id = Create("gone");

            using (var db = Open())
                db.Execute("UPDATE Users SET is_deleted = 1 WHERE user_id = @id", new { id });

            Assert.Null(_store.FindById(id));
            Assert.Null(_store.FindByEmail(Email("gone")));
            Assert.Throws<DuplicateEmailException>(() => _store.CreateUser(New("gone")));
            Assert.False(_store.UpdateAccount(id, "X", Email("gone"), null));
            Assert.False(_store.SetPasswordHash(id, "$2a$04$otherhashotherhashothe"));
        }

        [DbFact]
        public void Updating_an_account_changes_only_name_email_and_company()
        {
            var id = Create("keep", "employer", "Acme");

            var before = Value<(string Role, string Hash, DateTime Created, bool Deleted)>(
                "SELECT role AS Role, password_hash AS Hash, created_at AS Created, CAST(is_deleted AS bit) AS Deleted FROM Users WHERE user_id = @id", new { id });

            Assert.True(_store.UpdateAccount(id, "New Name", Email("keep2"), "Globex"));

            var after = _store.FindById(id)!;

            Assert.Equal("New Name", after.FullName);
            Assert.Equal(Email("keep2"), after.Email);
            Assert.Equal("Globex", after.CompanyName);

            Assert.Equal(before.Role, after.Role);
            Assert.Equal(before.Hash, after.PasswordHash);
            Assert.Equal(before.Created, after.CreatedAt);
            Assert.False(after.IsDeleted);
        }

        [DbFact]
        public void Updating_only_touches_the_one_user()
        {
            var mine = Create("mine");
            var theirs = Create("theirs");

            _store.UpdateAccount(mine, "Changed", Email("mine"), null);

            Assert.Equal($"dbtest {_tag}", _store.FindById(theirs)!.FullName);
        }

        [DbFact]
        public void An_email_someone_else_has_is_refused_in_any_case()
        {
            var mine = Create("m");
            Create("t");

            Assert.Throws<DuplicateEmailException>(() => _store.UpdateAccount(mine, "X", Email("t").ToUpperInvariant(), null));
            Assert.Equal(Email("m"), _store.FindById(mine)!.Email);
        }

        [DbFact]
        public void Setting_a_password_hash_changes_only_the_hash()
        {
            var id = Create("pw");

            Assert.True(_store.SetPasswordHash(id, "$2a$04$brandnewhashbrandnewh"));

            var after = _store.FindById(id)!;

            Assert.Equal("$2a$04$brandnewhashbrandnewh", after.PasswordHash);
            Assert.Equal($"dbtest {_tag}", after.FullName);
            Assert.Equal(Email("pw"), after.Email);
            Assert.Equal("user", after.Role);
        }

        [DbFact]
        public void An_email_with_quotes_and_percent_signs_is_treated_as_data()
        {
            Create("safe");

            Assert.Null(_store.FindByEmail("' OR 1=1 --"));
            Assert.Null(_store.FindByEmail("%"));
            Assert.Null(_store.FindByEmail(""));
        }
    }
}
