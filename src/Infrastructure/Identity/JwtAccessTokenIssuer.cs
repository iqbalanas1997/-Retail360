using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Retail360.Application.Identity;

namespace Retail360.Infrastructure.Identity;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly ShopAuthenticationSettings _settings;

    public JwtAccessTokenIssuer(ShopAuthenticationSettings settings)
    {
        _settings = settings;
    }

    public IssuedAccessToken Issue(
        string userId,
        string userName,
        IReadOnlyList<string> roles,
        DateTimeOffset issuedAt)
    {
        var expires = issuedAt.AddMinutes(_settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("unique_name", userName)
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _settings.Issuer,
            _settings.Audience,
            claims,
            issuedAt.UtcDateTime,
            expires.UtcDateTime,
            credentials);
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
