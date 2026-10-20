using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelXpense.Api.Contracts;
using TravelXpense.Api.Data;
using TravelXpense.Api.Domain;
using TravelXpense.Api.Security;

namespace TravelXpense.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(AppDbContext db, JwtTokenService tokens) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new ProblemDetails { Title = "That email is already registered." });

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = req.FullName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password), // never store plain passwords
            Role = UserRole.Employee,                                    // self-registration can NEVER create a manager/admin
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Ok(tokens.Issue(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Same message for "no such user" and "wrong password" so attackers can't discover which emails exist.
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new ProblemDetails { Title = "Invalid email or password." });

        return Ok(tokens.Issue(user));
    }
}
