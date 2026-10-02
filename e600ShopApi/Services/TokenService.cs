using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using e600ShopApi.Domain;
using Microsoft.IdentityModel.Tokens;

namespace e600ShopApi.Services;

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    public TokenResponse GenerateToken(User user)
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured. Set it with 'dotnet user-secrets set \"Jwt:Secret\" <value>' " +
                "or the Jwt__Secret environment variable.");
        }

        var issuer = configuration["Jwt:Issuer"] ?? "e600ShopApi";
        var audience = configuration["Jwt:Audience"] ?? "e600Shop";
        var expirationMinutes =
            int.TryParse(configuration["Jwt:ExpirationMinutes"], out var minutes) && minutes > 0
                ? minutes
                : 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
