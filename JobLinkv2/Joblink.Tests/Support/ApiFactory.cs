using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using JobLinkv2.Services.Apply;
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
    public sealed class ApiFactory : WebApplicationFactory<Program>
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
        public TestClock Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IApplyStore>();
                services.AddSingleton<IApplyStore>(Store);

                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
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
