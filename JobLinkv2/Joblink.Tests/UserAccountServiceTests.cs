using Joblink.Services.Accounts;
using Joblink.Tests.Support;
using Xunit;

namespace Joblink.Tests
{
    // Sign up, log in and changing your own details, against an in-memory store
    // (bcrypt work factor 4 so the tests stay fast).
    public class UserAccountServiceTests
    {
        private readonly InMemoryUserStore _store = new();
        private readonly TestClock _clock = new();
        private readonly UserAccountService _service;

        public UserAccountServiceTests()
        {
            _service = new UserAccountService(_store, _clock, bcryptWorkFactor: 4);
        }

        private static SignupRequest Signup(string email = "maria@example.com", string role = "user", string? company = null) => new()
        {
            FullName = "Maria Santos", Email = email, Password = "Sup3rSecret!", Role = role, CompanyName = company
        };

        // ----- sign up ----------------------------------------------------------

        [Fact]
        public void Signup_stores_a_bcrypt_hash_never_the_password()
        {
            var result = _service.Signup(Signup());

            Assert.True(result.IsOk);

            var saved = Assert.Single(_store.Users);

            Assert.NotEqual("Sup3rSecret!", saved.PasswordHash);
            Assert.StartsWith("$2", saved.PasswordHash);
            Assert.True(BCrypt.Net.BCrypt.Verify("Sup3rSecret!", saved.PasswordHash));
        }

        [Fact]
        public void Signup_sets_the_fields_the_server_owns()
        {
            var id = _service.Signup(Signup()).Value;

            var saved = _store.Users.Single(u => u.UserId == id);

            Assert.False(saved.IsDeleted);
            Assert.Equal("user", saved.Role);
            Assert.Null(saved.CompanyName);
            Assert.Equal(_clock.GetLocalNow().DateTime, saved.CreatedAt);
        }

        [Theory]
        [InlineData(null, "user")]
        [InlineData("", "user")]
        [InlineData("   ", "user")]
        [InlineData("user", "user")]
        [InlineData("USER", "user")]
        [InlineData(" Employer ", "employer")]
        public void Signup_accepts_only_the_two_account_types(string? asked, string stored)
        {
            var result = _service.Signup(Signup(role: asked!, company: "Acme"));

            Assert.True(result.IsOk);
            Assert.Equal(stored, _store.Users.Single().Role);
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("Admin")]
        [InlineData("administrator")]
        [InlineData("superuser")]
        [InlineData("string")]
        [InlineData("user,admin")]
        public void Signup_refuses_any_other_role(string asked)
        {
            var result = _service.Signup(Signup(role: asked, company: "Acme"));

            Assert.Equal(AccountOutcome.Invalid, result.Outcome);
            Assert.Empty(_store.Users);
        }

        [Fact]
        public void A_job_seeker_never_gets_a_company_name()
        {
            _service.Signup(Signup(role: "user", company: "Sneaky Corp"));

            Assert.Null(_store.Users.Single().CompanyName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_employer_needs_a_company_name(string? company)
        {
            var result = _service.Signup(Signup(role: "employer", company: company));

            Assert.Equal(AccountOutcome.Invalid, result.Outcome);
            Assert.Empty(_store.Users);
        }

        [Fact]
        public void An_employer_signs_up_with_a_trimmed_company_name()
        {
            _service.Signup(Signup(role: "employer", company: "  Acme Corp  "));

            Assert.Equal("Acme Corp", _store.Users.Single().CompanyName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("short")]
        [InlineData("1234567")]
        public void Signup_refuses_short_passwords(string password)
        {
            var request = Signup();
            request.Password = password;

            var result = _service.Signup(request);

            Assert.Equal(AccountOutcome.Invalid, result.Outcome);
            Assert.Empty(_store.Users);
        }

        [Fact]
        public void Signup_refuses_passwords_bcrypt_would_silently_cut_short()
        {
            var request = Signup();
            request.Password = new string('a', 73);

            Assert.Equal(AccountOutcome.Invalid, _service.Signup(request).Outcome);

            request.Password = new string('a', 72);

            Assert.True(_service.Signup(request).IsOk);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-an-email")]
        [InlineData("two@@example.com")]
        [InlineData("spaces in@example.com")]
        [InlineData("Maria <maria@example.com>")]
        public void Signup_refuses_bad_emails(string? email)
        {
            var result = _service.Signup(Signup(email: email!));

            Assert.Equal(AccountOutcome.Invalid, result.Outcome);
            Assert.Empty(_store.Users);
        }

        [Fact]
        public void Signup_refuses_values_longer_than_the_columns()
        {
            var longName = Signup();
            longName.FullName = new string('n', 101);

            var longEmail = Signup(email: new string('e', 95) + "@x.com");

            var longCompany = Signup(role: "employer", company: new string('c', 256));

            Assert.Equal(AccountOutcome.Invalid, _service.Signup(longName).Outcome);
            Assert.Equal(AccountOutcome.Invalid, _service.Signup(longEmail).Outcome);
            Assert.Equal(AccountOutcome.Invalid, _service.Signup(longCompany).Outcome);
            Assert.Empty(_store.Users);
        }

        [Fact]
        public void Signup_needs_a_name_and_a_request()
        {
            var noName = Signup();
            noName.FullName = "  ";

            Assert.Equal(AccountOutcome.Invalid, _service.Signup(noName).Outcome);
            Assert.Equal(AccountOutcome.Invalid, _service.Signup(null).Outcome);
        }

        [Theory]
        [InlineData("maria@example.com")]
        [InlineData("MARIA@EXAMPLE.COM")]
        [InlineData("  maria@example.com  ")]
        public void The_same_email_can_only_sign_up_once(string second)
        {
            Assert.True(_service.Signup(Signup()).IsOk);

            var result = _service.Signup(Signup(email: second));

            Assert.Equal(AccountOutcome.Duplicate, result.Outcome);
            Assert.Single(_store.Users);
        }

        [Fact]
        public void The_email_of_a_deleted_account_stays_reserved()
        {
            _store.AddUser("gone@example.com", isDeleted: true);

            var result = _service.Signup(Signup(email: "gone@example.com"));

            Assert.Equal(AccountOutcome.Duplicate, result.Outcome);
        }

        // ----- log in -----------------------------------------------------------

        [Theory]
        [InlineData("maria@example.com")]
        [InlineData("MARIA@example.com")]
        [InlineData("  maria@example.com ")]
        public void Login_works_whatever_the_email_capitals(string email)
        {
            var user = _store.AddUser("maria@example.com");

            var result = _service.Login(email, "Sup3rSecret!");

            Assert.True(result.IsOk);
            Assert.Equal(user.UserId, result.Value!.UserId);
        }

        [Fact]
        public void An_unknown_email_and_a_wrong_password_look_exactly_the_same()
        {
            _store.AddUser("maria@example.com");

            var unknown = _service.Login("nobody@example.com", "Sup3rSecret!");
            var wrong = _service.Login("maria@example.com", "not-the-password");

            Assert.Equal(AccountOutcome.Unauthorized, unknown.Outcome);
            Assert.Equal(AccountOutcome.Unauthorized, wrong.Outcome);
            Assert.Equal(unknown.Message, wrong.Message);
            Assert.Equal("Invalid email or password", unknown.Message);
        }

        [Theory]
        [InlineData(null, "x")]
        [InlineData("", "x")]
        [InlineData("maria@example.com", null)]
        [InlineData("maria@example.com", "")]
        public void Login_needs_both_fields(string? email, string? password)
        {
            _store.AddUser("maria@example.com");

            Assert.Equal(AccountOutcome.Unauthorized, _service.Login(email, password).Outcome);
        }

        [Fact]
        public void A_deleted_account_cannot_log_in()
        {
            _store.AddUser("maria@example.com", isDeleted: true);

            Assert.Equal(AccountOutcome.Unauthorized, _service.Login("maria@example.com", "Sup3rSecret!").Outcome);
        }

        [Fact]
        public void An_old_plain_text_password_still_works_once_and_is_hashed_on_the_spot()
        {
            var user = _store.AddUser("old@example.com", password: "letmein-2019", legacyPlainTextPassword: true);

            var first = _service.Login("old@example.com", "letmein-2019");

            Assert.True(first.IsOk);

            var saved = Assert.Single(_store.SavedPasswordHashes);

            Assert.Equal(user.UserId, saved.UserId);
            Assert.StartsWith("$2", saved.Hash);
            Assert.True(BCrypt.Net.BCrypt.Verify("letmein-2019", _store.Users.Single().PasswordHash));

            // and it keeps working through the normal bcrypt path
            Assert.True(_service.Login("old@example.com", "letmein-2019").IsOk);
            Assert.Single(_store.SavedPasswordHashes);
        }

        [Fact]
        public void A_wrong_guess_at_an_old_plain_text_password_changes_nothing()
        {
            _store.AddUser("old@example.com", password: "letmein-2019", legacyPlainTextPassword: true);

            var result = _service.Login("old@example.com", "letmein-2020");

            Assert.Equal(AccountOutcome.Unauthorized, result.Outcome);
            Assert.Empty(_store.SavedPasswordHashes);
            Assert.Equal("letmein-2019", _store.Users.Single().PasswordHash);
        }

        // ----- changing your own details ---------------------------------------

        [Fact]
        public void You_can_change_your_name_without_a_password()
        {
            var user = _store.AddUser("maria@example.com");

            var result = _service.UpdateAccount(user.UserId, new UpdateAccountRequest { FullName = "  Maria S. Santos " });

            Assert.True(result.IsOk);
            Assert.Equal("Maria S. Santos", _store.Users.Single().FullName);
            Assert.Equal("maria@example.com", _store.Users.Single().Email);
        }

        [Fact]
        public void Fields_that_are_not_sent_stay_as_they_are()
        {
            var user = _store.AddUser("maria@example.com", fullName: "Maria Santos");

            var result = _service.UpdateAccount(user.UserId, new UpdateAccountRequest());

            Assert.True(result.IsOk);
            Assert.Equal("Maria Santos", _store.Users.Single().FullName);
            Assert.Equal("maria@example.com", _store.Users.Single().Email);
        }

        [Fact]
        public void Changing_the_email_needs_the_current_password()
        {
            var user = _store.AddUser("maria@example.com");

            var result = _service.UpdateAccount(user.UserId, new UpdateAccountRequest { Email = "new@example.com" });

            Assert.Equal(AccountOutcome.PasswordRequired, result.Outcome);
            Assert.Equal("maria@example.com", _store.Users.Single().Email);
        }

        [Fact]
        public void A_wrong_current_password_changes_nothing()
        {
            var user = _store.AddUser("maria@example.com");

            var result = _service.UpdateAccount(user.UserId, new UpdateAccountRequest
            {
                FullName = "Hijacked", Email = "new@example.com", CurrentPassword = "guess"
            });

            Assert.Equal(AccountOutcome.WrongPassword, result.Outcome);
            Assert.Equal("maria@example.com", _store.Users.Single().Email);
            Assert.Equal("Test User", _store.Users.Single().FullName);
        }

        [Fact]
        public void The_right_current_password_changes_the_email()
        {
            var user = _store.AddUser("maria@example.com");

            var result = _service.UpdateAccount(user.UserId, new UpdateAccountRequest
            {
                Email = " New@Example.com ", CurrentPassword = "Sup3rSecret!"
            });

            Assert.True(result.IsOk);
            Assert.Equal("New@Example.com", _store.Users.Single().Email);
            Assert.Equal("New@Example.com", result.Value!.Email);
        }

        [Fact]
        public void Only_changing_the_capitals_of_your_email_needs_no_password()
        {
            var user = _store.AddUser("maria@example.com");

            var result = _service.UpdateAccount(user.UserId, new UpdateAccountRequest { Email = "Maria@Example.com" });

            Assert.True(result.IsOk);
            Assert.Equal("Maria@Example.com", _store.Users.Single().Email);
        }

        [Fact]
        public void You_cannot_take_an_email_someone_else_has()
        {
            var me = _store.AddUser("maria@example.com");
            _store.AddUser("taken@example.com");

            var result = _service.UpdateAccount(me.UserId, new UpdateAccountRequest
            {
                Email = "TAKEN@example.com", CurrentPassword = "Sup3rSecret!"
            });

            Assert.Equal(AccountOutcome.Duplicate, result.Outcome);
            Assert.Equal("maria@example.com", _store.Users.First().Email);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void You_cannot_blank_your_name(string name)
        {
            var user = _store.AddUser("maria@example.com");

            Assert.Equal(AccountOutcome.Invalid, _service.UpdateAccount(user.UserId, new UpdateAccountRequest { FullName = name }).Outcome);
            Assert.Equal("Test User", _store.Users.Single().FullName);
        }

        [Fact]
        public void A_bad_email_is_refused_before_the_password_is_even_asked_for()
        {
            var user = _store.AddUser("maria@example.com");

            Assert.Equal(AccountOutcome.Invalid, _service.UpdateAccount(user.UserId, new UpdateAccountRequest { Email = "nope" }).Outcome);
        }

        [Fact]
        public void An_employer_can_change_the_company_name_but_not_blank_it()
        {
            var boss = _store.AddUser("boss@example.com", role: "employer", companyName: "Acme");

            Assert.True(_service.UpdateAccount(boss.UserId, new UpdateAccountRequest { CompanyName = " Globex " }).IsOk);
            Assert.Equal("Globex", _store.Users.Single().CompanyName);

            Assert.Equal(AccountOutcome.Invalid, _service.UpdateAccount(boss.UserId, new UpdateAccountRequest { CompanyName = "  " }).Outcome);
            Assert.Equal("Globex", _store.Users.Single().CompanyName);
        }

        [Fact]
        public void A_job_seeker_cannot_give_themselves_a_company()
        {
            var user = _store.AddUser("maria@example.com");

            _service.UpdateAccount(user.UserId, new UpdateAccountRequest { CompanyName = "Sneaky Corp" });

            Assert.Null(_store.Users.Single().CompanyName);
        }

        [Fact]
        public void Updating_never_touches_the_role_the_hash_or_the_deleted_flag()
        {
            var user = _store.AddUser("maria@example.com");
            var hash = _store.Users.Single().PasswordHash;

            _service.UpdateAccount(user.UserId, new UpdateAccountRequest { FullName = "Maria S." });

            var saved = _store.Users.Single();

            Assert.Equal("user", saved.Role);
            Assert.Equal(hash, saved.PasswordHash);
            Assert.False(saved.IsDeleted);
            Assert.Equal(new DateTime(2026, 1, 1), saved.CreatedAt);
            Assert.Empty(_store.SavedPasswordHashes);
        }

        [Fact]
        public void A_missing_or_deleted_account_cannot_be_updated()
        {
            var gone = _store.AddUser("gone@example.com", isDeleted: true);

            Assert.Equal(AccountOutcome.NotFound, _service.UpdateAccount(gone.UserId, new UpdateAccountRequest { FullName = "X" }).Outcome);
            Assert.Equal(AccountOutcome.NotFound, _service.UpdateAccount(9999, new UpdateAccountRequest { FullName = "X" }).Outcome);
        }
    }
}
