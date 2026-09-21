using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using JobLinkv2.Models;
using Microsoft.IdentityModel.Tokens;

namespace Joblink.Security
{
    public sealed class JwtTokenService
    {
        private readonly JwtOptions _options;
        private readonly TimeProvider _time;

        public JwtTokenService(JwtOptions options, TimeProvider time)
        {
            _options = options;
            _time = time;
        }

        // The token carries only who the caller is and their role - nothing else.
        public string CreateToken(int userId, string? role)
        {
            var now = _time.GetUtcNow().UtcDateTime;

            // Standard short claim names. The API maps sub -> NameIdentifier and
            // role -> Role when it reads the token back.
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new("role", role ?? "user")
            };

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(_options.ExpiryMinutes),
                signingCredentials: new SigningCredentials(_options.SigningKey, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string CreateToken(UserModel user) => CreateToken(user.UserId, user.Role);
    }

    public static class ClaimsPrincipalExtensions
    {
        // The logged-in user's id from their token, or null if there isn't one.
        public static int? GetUserId(this ClaimsPrincipal principal) =>
            int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
