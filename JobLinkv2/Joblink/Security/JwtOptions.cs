using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Joblink.Security
{
    // Settings for the login tokens. The signing key is a secret: it comes from
    // "Jwt:Key" - `dotnet user-secrets` while developing, the Jwt__Key
    // environment variable in production - and is never stored in the repo.
    public sealed class JwtOptions
    {
        public const int MinKeyLength = 32;

        public string Key { get; init; } = "";
        public string Issuer { get; init; } = "JobLink";
        public string Audience { get; init; } = "JobLink.Web";
        public int ExpiryMinutes { get; init; } = 480;

        public static JwtOptions From(IConfiguration configuration, IHostEnvironment environment)
        {
            var key = configuration["Jwt:Key"];

            if (string.IsNullOrWhiteSpace(key))
            {
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        "Jwt:Key is not configured. Set the Jwt__Key environment variable to a random secret " +
                        $"of at least {MinKeyLength} characters (for example: openssl rand -base64 64).");
                }

                // Development only: a throwaway key so the API still starts on a fresh
                // checkout. Everyone is logged out whenever the API restarts.
                key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

                Console.Error.WriteLine(
                    "[warn] Jwt:Key is not set - using a temporary key, so logins won't survive a restart. " +
                    "Run: dotnet user-secrets set \"Jwt:Key\" \"<random secret>\"");
            }

            if (key.Length < MinKeyLength)
                throw new InvalidOperationException($"Jwt:Key must be at least {MinKeyLength} characters long.");

            return new JwtOptions
            {
                Key = key,
                Issuer = configuration["Jwt:Issuer"] ?? "JobLink",
                Audience = configuration["Jwt:Audience"] ?? "JobLink.Web",
                ExpiryMinutes = int.TryParse(configuration["Jwt:ExpiryMinutes"], out var minutes) && minutes > 0 ? minutes : 480
            };
        }

        public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));

        public TokenValidationParameters ToValidationParameters() => new()
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier
        };
    }
}
