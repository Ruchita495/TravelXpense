using Microsoft.EntityFrameworkCore;
using TravelXpense.Api.Domain;

namespace TravelXpense.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();
    public DbSet<ExpenseItem> ExpenseItems => Set<ExpenseItem>();
    public DbSet<PolicyRule> PolicyRules => Set<PolicyRule>();
    public DbSet<PolicyViolation> PolicyViolations => Set<PolicyViolation>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(100);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Trip>(e =>
        {
            e.Property(x => x.Destination).HasMaxLength(200);
            e.Property(x => x.Purpose).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.EmployeeId);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Booking>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(20);
            e.Property(x => x.Provider).HasMaxLength(100);
            e.Property(x => x.Reference).HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Cabin).HasMaxLength(20);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasOne<Trip>().WithMany(t => t.Bookings).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExpenseReport>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ReviewerComment).HasMaxLength(1000);
            e.HasIndex(x => new { x.EmployeeId, x.Status });
            e.HasOne(x => x.Trip).WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ExpenseItem>(e =>
        {
            e.Property(x => x.Merchant).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ReceiptUrl).HasMaxLength(500);
            e.HasOne<ExpenseReport>().WithMany(r => r.Items).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PolicyRule>(e =>
        {
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.MaxAmount).HasPrecision(18, 2);
            e.Property(x => x.Description).HasMaxLength(300);
            e.HasData(
                Rule("00000000-0000-0000-0000-000000000001", ExpenseCategory.Airfare, 1500m, "Airfare cap per ticket (economy)"),
                Rule("00000000-0000-0000-0000-000000000002", ExpenseCategory.Lodging, 300m, "Hotel cap per night"),
                Rule("00000000-0000-0000-0000-000000000003", ExpenseCategory.Meals, 75m, "Meal cap per receipt"),
                Rule("00000000-0000-0000-0000-000000000004", ExpenseCategory.GroundTransport, 150m, "Ground transport cap per receipt"),
                Rule("00000000-0000-0000-0000-000000000005", ExpenseCategory.Other, 100m, "Other expenses cap per receipt"));
        });

        b.Entity<PolicyViolation>(e =>
        {
            e.Property(x => x.Message).HasMaxLength(500);
            e.HasOne<ExpenseReport>().WithMany(r => r.Violations).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProcessedMessage>(e =>
        {
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(64);
            e.Property(x => x.Handler).HasMaxLength(100);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(1000);
        });
    }

    private static PolicyRule Rule(string id, ExpenseCategory c, decimal max, string text) =>
        new() { Id = Guid.Parse(id), Category = c, MaxAmount = max, Description = text, Active = true };
}
