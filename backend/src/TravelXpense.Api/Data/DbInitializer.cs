using Microsoft.EntityFrameworkCore;
using TravelXpense.Api.Domain;

namespace TravelXpense.Api.Data;

public static class DbInitializer
{
    /// <summary>Creates the schema (if missing), waits for SQL Server to be ready, and seeds demo users in development.</summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration cfg)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        // The SQL Server container can take ~20-30s to accept connections, so retry.
        for (var attempt = 1; ; attempt++)
        {
            try { await db.Database.EnsureCreatedAsync(); break; }
            catch (Exception ex) when (attempt < 20)
            {
                log.LogWarning("Database not ready yet (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        if (!cfg.GetValue<bool>("Seed:DemoUsers") || await db.Users.AnyAsync()) return;

        var manager = new User
        {
            Id = Guid.NewGuid(), Email = "manager@demo.com", FullName = "Morgan Manager",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"), Role = UserRole.Manager, CreatedAt = DateTime.UtcNow
        };
        var employee = new User
        {
            Id = Guid.NewGuid(), Email = "employee@demo.com", FullName = "Erin Employee",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"), Role = UserRole.Employee,
            ManagerId = manager.Id, CreatedAt = DateTime.UtcNow
        };
        db.Users.AddRange(manager, employee);
        await db.SaveChangesAsync();
        log.LogInformation("Seeded demo users manager@demo.com and employee@demo.com (password: Password123!)");
    }
}
