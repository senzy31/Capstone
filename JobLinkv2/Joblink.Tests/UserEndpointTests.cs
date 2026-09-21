using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using JobLinkv2.Models;
using Joblink.Tests.Support;
using Xunit;

namespace Joblink.Tests
{
    // /api/User used to hand out every user's password hash, let anyone choose a
    // role at signup, and let anyone rewrite any column of any user with one PUT.
    // These go through real routing, real JWT checks and the real controller.
    public class UserEndpointTests : EndpointTestBase
    {
        public UserEndpointTests(ApiFactory factory) : base(factory) { }

        private InMemoryUserStore Users => Factory.UserStore;

        private static string NewEmail() => $"u{Guid.NewGuid():N}@example.com";

        private UserModel Seed(string role = "user", string? company = null, string password = "Sup3rSecret!") =>
            Users.AddUser(NewEmail(), password, role, companyName: company);

        private HttpClient As(UserModel user) => Client(user.UserId, user.Role);

        private static string[] Names(JsonElement element) =>
            element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // ----- what no longer exists -------------------------------------------

        [Fact]
        public async Task There_is_no_list_of_all_users()
        {
            Seed();

            var anonymous = await Send(Factory.CreateClient(), HttpMethod.Get, "/api/User");
            var loggedIn = await Send(As(Seed()), HttpMethod.Get, "/api/User");

            Assert.Equal(HttpStatusCode.MethodNotAllowed, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, loggedIn.StatusCode);
        }

        [Fact]
        public async Task Nobody_can_delete_a_user_not_even_themselves()
        {
            var me = Seed();
            var other = Seed();

            var anonymous = await Send(Factory.CreateClient(), HttpMethod.Delete, $"/api/User/{other.UserId}");
            var others = await Send(As(me), HttpMethod.Delete, $"/api/User/{other.UserId}");
            var mine = await Send(As(me), HttpMethod.Delete, $"/api/User/{me.UserId}");

            Assert.Equal(HttpStatusCode.MethodNotAllowed, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, others.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, mine.StatusCode);
            Assert.False(other.IsDeleted);
            Assert.False(me.IsDeleted);
        }

        // ----- reading your own account ----------------------------------------

        [Fact]
        public async Task Reading_an_account_needs_a_login()
        {
            var user = Seed();

            var response = await Send(Factory.CreateClient(), HttpMethod.Get, $"/api/User/{user.UserId}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task You_can_read_your_own_account_and_it_never_includes_the_password_hash()
        {
            var me = Seed(company: null);

            var response = await Send(As(me), HttpMethod.Get, $"/api/User/{me.UserId}");
            var text = await response.Content.ReadAsStringAsync();
            var body = JsonDocument.Parse(text).RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "companyName", "createdAt", "email", "fullName", "role", "userId" }, Names(body));
            Assert.Equal(me.Email, body.GetProperty("email").GetString());
            Assert.DoesNotContain("assword", text);
            Assert.DoesNotContain(me.PasswordHash, text);
        }

        [Fact]
        public async Task You_cannot_read_someone_elses_account()
        {
            var me = Seed();
            var other = Seed();

            var response = await Send(As(me), HttpMethod.Get, $"/api/User/{other.UserId}");
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain(other.Email!, text);
        }

        [Fact]
        public async Task An_employer_can_read_their_own_account_too()
        {
            var boss = Seed("employer", "Acme");

            var response = await Send(As(boss), HttpMethod.Get, $"/api/User/{boss.UserId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Acme", (await Read(response)).GetProperty("companyName").GetString());
        }

        // ----- sign up ----------------------------------------------------------

        [Fact]
        public async Task Signup_creates_a_job_seeker_and_never_stores_the_password()
        {
            var email = NewEmail();

            var response = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User",
                $$"""{"fullName":"Maria Santos","email":"{{email}}","password":"Sup3rSecret!"}""");

            var saved = Users.Users.Single(u => u.Email == email);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("user", saved.Role);
            Assert.StartsWith("$2", saved.PasswordHash);
            Assert.True(BCrypt.Net.BCrypt.Verify("Sup3rSecret!", saved.PasswordHash));
        }

        [Fact]
        public async Task Signup_ignores_everything_a_client_should_not_choose()
        {
            var email = NewEmail();

            var response = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User", $$"""
                {
                  "fullName": "Mallory",
                  "email": "{{email}}",
                  "password": "Sup3rSecret!",
                  "passwordHash": "plain-text-i-picked",
                  "userId": 1,
                  "isDeleted": true,
                  "createdAt": "2000-01-01T00:00:00",
                  "plan": "premium",
                  "premiumUntil": "2099-12-31T00:00:00",
                  "subscription": { "tier": "gold" },
                  "companyName": "Sneaky Corp"
                }
                """);

            var saved = Users.Users.Single(u => u.Email == email);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("user", saved.Role);
            Assert.False(saved.IsDeleted);
            Assert.Null(saved.CompanyName);
            Assert.NotEqual(1, saved.UserId);
            Assert.NotEqual(new DateTime(2000, 1, 1), saved.CreatedAt);
            Assert.NotEqual("plain-text-i-picked", saved.PasswordHash);
            Assert.True(BCrypt.Net.BCrypt.Verify("Sup3rSecret!", saved.PasswordHash));
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("Administrator")]
        [InlineData("string")]
        public async Task Signup_cannot_create_any_other_kind_of_account(string role)
        {
            var email = NewEmail();

            var response = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User",
                $$"""{"fullName":"Mallory","email":"{{email}}","password":"Sup3rSecret!","role":"{{role}}"}""");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain(Users.Users, u => u.Email == email);
        }

        [Fact]
        public async Task Signup_as_an_employer_keeps_the_company_name()
        {
            var email = NewEmail();

            var response = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User",
                $$"""{"fullName":"Rico","email":"{{email}}","password":"Sup3rSecret!","role":"employer","companyName":"Acme Corp"}""");

            var saved = Users.Users.Single(u => u.Email == email);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("employer", saved.Role);
            Assert.Equal("Acme Corp", saved.CompanyName);
        }

        [Fact]
        public async Task Signup_with_a_taken_email_is_a_409_with_a_plain_text_message()
        {
            var taken = Seed();

            var response = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User",
                $$"""{"fullName":"Copycat","email":"{{taken.Email!.ToUpperInvariant()}}","password":"Sup3rSecret!"}""");

            // The signup page shows response.text() in an alert, so it has to be text.
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Signup_validation_errors_are_plain_text_too()
        {
            var response = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User",
                $$"""{"fullName":"Shorty","email":"{{NewEmail()}}","password":"short"}""");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("at least 8", await response.Content.ReadAsStringAsync());
        }

        // ----- log in -----------------------------------------------------------

        [Fact]
        public async Task Login_returns_a_token_for_that_user_and_never_the_hash()
        {
            var me = Seed("employer", "Acme");

            var response = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User/login",
                $$"""{"email":"{{me.Email}}","password":"Sup3rSecret!"}""");

            var text = await response.Content.ReadAsStringAsync();
            var body = JsonDocument.Parse(text).RootElement;
            var token = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("token").GetString());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(me.UserId.ToString(), token.Claims.Single(c => c.Type == "sub").Value);
            Assert.Equal("employer", token.Claims.Single(c => c.Type == "role").Value);
            Assert.Equal(new[] { "companyName", "email", "fullName", "role", "userId" }, Names(body.GetProperty("user")));
            Assert.DoesNotContain(me.PasswordHash, text);
        }

        [Fact]
        public async Task Login_says_the_same_thing_for_an_unknown_email_and_a_wrong_password()
        {
            var me = Seed();

            var unknown = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User/login",
                $$"""{"email":"{{NewEmail()}}","password":"Sup3rSecret!"}""");
            var wrong = await Send(Factory.CreateClient(), HttpMethod.Post, "/api/User/login",
                $$"""{"email":"{{me.Email}}","password":"not-it"}""");

            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        }

        // ----- changing your own details ---------------------------------------

        [Fact]
        public async Task Changing_your_account_needs_a_login()
        {
            var response = await Send(Factory.CreateClient(), HttpMethod.Put, "/api/User", """{"fullName":"Anon"}""");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // The attack this whole change exists to stop: one PUT that names another
        // user's id and every column an attacker would like to set.
        [Fact]
        public async Task An_update_can_only_ever_change_your_own_name_email_and_company()
        {
            var attacker = Seed();
            var victim = Seed(password: "VictimPassw0rd!");
            var attackerHash = attacker.PasswordHash;
            var victimHash = victim.PasswordHash;

            var response = await Send(As(attacker), HttpMethod.Put, "/api/User", $$"""
                {
                  "userId": {{victim.UserId}},
                  "fullName": "Mallory Was Here",
                  "email": "{{attacker.Email}}",
                  "role": "admin",
                  "passwordHash": "plain-text-i-picked",
                  "password": "plain-text-i-picked",
                  "isDeleted": true,
                  "createdAt": "2000-01-01T00:00:00",
                  "plan": "premium",
                  "premiumUntil": "2099-12-31T00:00:00",
                  "companyName": "Sneaky Corp"
                }
                """);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // the caller's own name changed - and nothing else about them
            Assert.Equal("Mallory Was Here", attacker.FullName);
            Assert.Equal("user", attacker.Role);
            Assert.Equal(attackerHash, attacker.PasswordHash);
            Assert.False(attacker.IsDeleted);
            Assert.Equal(new DateTime(2026, 1, 1), attacker.CreatedAt);
            Assert.Null(attacker.CompanyName);

            // the user named in the body was not touched at all
            Assert.Equal("Test User", victim.FullName);
            Assert.Equal("user", victim.Role);
            Assert.Equal(victimHash, victim.PasswordHash);
            Assert.False(victim.IsDeleted);
            Assert.True(BCrypt.Net.BCrypt.Verify("VictimPassw0rd!", victim.PasswordHash));
        }

        [Fact]
        public async Task An_update_answers_with_the_account_and_no_hash()
        {
            var me = Seed();

            var response = await Send(As(me), HttpMethod.Put, "/api/User", """{"fullName":"Maria S."}""");
            var text = await response.Content.ReadAsStringAsync();
            var body = JsonDocument.Parse(text).RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Maria S.", body.GetProperty("user").GetProperty("fullName").GetString());
            Assert.Equal(new[] { "companyName", "createdAt", "email", "fullName", "role", "userId" }, Names(body.GetProperty("user")));
            Assert.DoesNotContain(me.PasswordHash, text);
        }

        [Fact]
        public async Task Changing_your_email_asks_for_the_current_password_first()
        {
            var me = Seed();
            var oldEmail = me.Email;

            var response = await Send(As(me), HttpMethod.Put, "/api/User", $$"""{"email":"{{NewEmail()}}"}""");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("password_required", (await Read(response)).GetProperty("code").GetString());
            Assert.Equal(oldEmail, me.Email);
        }

        [Fact]
        public async Task A_wrong_current_password_is_a_403_never_a_401()
        {
            // The pages treat a 401 as "your login expired" and sign the user out.
            var me = Seed();
            var oldEmail = me.Email;

            var response = await Send(As(me), HttpMethod.Put, "/api/User",
                $$"""{"email":"{{NewEmail()}}","currentPassword":"guess"}""");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("wrong_password", (await Read(response)).GetProperty("code").GetString());
            Assert.Equal(oldEmail, me.Email);
        }

        [Fact]
        public async Task The_right_current_password_changes_the_email()
        {
            var me = Seed();
            var fresh = NewEmail();

            var response = await Send(As(me), HttpMethod.Put, "/api/User",
                $$"""{"email":"{{fresh}}","currentPassword":"Sup3rSecret!"}""");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(fresh, me.Email);
        }

        [Fact]
        public async Task You_cannot_take_an_email_that_is_already_in_use()
        {
            var me = Seed();
            var other = Seed();
            var oldEmail = me.Email;

            var response = await Send(As(me), HttpMethod.Put, "/api/User",
                $$"""{"email":"{{other.Email}}","currentPassword":"Sup3rSecret!"}""");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("email_taken", (await Read(response)).GetProperty("code").GetString());
            Assert.Equal(oldEmail, me.Email);
        }

        [Fact]
        public async Task An_employer_can_rename_their_company()
        {
            var boss = Seed("employer", "Acme");

            var response = await Send(As(boss), HttpMethod.Put, "/api/User", """{"companyName":"Globex"}""");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Globex", boss.CompanyName);
        }

        [Fact]
        public async Task Invalid_details_are_a_400_and_change_nothing()
        {
            var me = Seed();

            var response = await Send(As(me), HttpMethod.Put, "/api/User", """{"fullName":"   "}""");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid", (await Read(response)).GetProperty("code").GetString());
            Assert.Equal("Test User", me.FullName);
        }
    }
}
