using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TravelXpense.Api.Contracts;
using TravelXpense.Api.Domain;

namespace TravelXpense.Api.Security;

public class JwtOptions
{
    public string Issuer { get; set; } = "TravelXpense";
    public string Audience { get; set; } = "TravelXpense.Client";
    public string Key { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 120;
}

public class JwtTokenService(JwtOptions options)
{
    public AuthResponse Issue(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.ExpiryMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Email, user.FullName, user.Role.ToString());
    }
}

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? throw new InvalidOperationException("Missing user id claim"));
}
