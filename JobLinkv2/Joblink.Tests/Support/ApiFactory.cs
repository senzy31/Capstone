using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Joblink.Services.Accounts;
using JobLinkv2.Services.Accounts;
using JobLinkv2.Services.Apply;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Resumes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Joblink.Tests.Support
{
    // Hosts the real API in memory with a fake store and clock - no database,
    // no network. Requests go through real routing, JWT authentication and the
    // real controllers.
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string SigningKey = "unit-test-signing-key-that-is-long-enough-1234567890";
        public const string Issuer = "JobLink";
        public const string Audience = "JobLink.Web";

        static ApiFactory()
        {
            // Read by Program.cs before the host is built, so it has to be an
            // environment variable (test settings arrive too late).
            Environment.SetEnvironmentVariable("Jwt__Key", SigningKey);
        }

        public InMemoryApplyStore Store { get; } = new();
        public InMemoryUserStore UserStore { get; } = new();

        // The stores for resumes, notifications, saved jobs and skills have no fake. Here they point at a server that is not
        // there, so a test that wrongly reaches it fails loudly instead of touching a real
        // database. DbApiFactory (the opt-in database tests) points it at the real one.
        protected virtual string DataConnectionString =>
            "Server=tcp:127.0.0.1,1; Database=unreachable; Trusted_Connection=true; Connect Timeout=1; Encrypt=false";

        public TestClock Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IApplyStore>();
                services.AddSingleton<IApplyStore>(Store);

                services.RemoveAll<IUserStore>();
                services.AddSingleton<IUserStore>(UserStore);

                services.RemoveAll<ResumeDataStore>();
                services.AddSingleton(new ResumeDataStore(DataConnectionString));

                services.RemoveAll<UserDataStore>();
                services.AddSingleton(new UserDataStore(DataConnectionString));

                services.RemoveAll<SkillStore>();
                services.AddSingleton(new SkillStore(DataConnectionString));

                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);

                // Work factor 4: hashing is real, just fast enough for tests.
                services.RemoveAll<UserAccountService>();
                services.AddSingleton(new UserAccountService(UserStore, Clock, bcryptWorkFactor: 4));
            });
        }

        public HttpClient ClientFor(int userId, string role = "user")
        {
            var client = CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", MakeToken(userId, role));

            return client;
        }

        public static string MakeToken(
            int userId,
            string role = "user",
            string key = SigningKey,
            string audience = Audience,
            TimeSpan? lifetime = null)
        {
            var now = DateTime.UtcNow;
            var life = lifetime ?? TimeSpan.FromHours(1);

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: audience,
                claims: new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                    new Claim("role", role)
                },
                // An "expired" token is issued in the past.
                notBefore: life < TimeSpan.Zero ? now + life - TimeSpan.FromHours(1) : now,
                expires: now + life,
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
