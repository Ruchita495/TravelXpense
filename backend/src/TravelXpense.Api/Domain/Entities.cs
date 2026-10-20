namespace TravelXpense.Api.Domain;

public enum UserRole { Employee, Manager, Admin }
public enum TripStatus { Active, Completed }
public enum ReportStatus { Draft, Submitted, PendingApproval, Flagged, Approved, Rejected }
public enum ExpenseCategory { Uncategorized, Airfare, Lodging, Meals, GroundTransport, Other }

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Trip
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public User? Employee { get; set; }
    public string Destination { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Purpose { get; set; } = "";
    public TripStatus Status { get; set; } = TripStatus.Active;
    public DateTime CreatedAt { get; set; }
    public List<Booking> Bookings { get; set; } = new();
}

public class Booking
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public string Type { get; set; } = "Flight";
    public string Provider { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "CAD";
    public string Cabin { get; set; } = "ECONOMY";
    public DateTime CreatedAt { get; set; }
}

public class ExpenseReport
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid EmployeeId { get; set; }
    public User? Employee { get; set; }
    public string Title { get; set; } = "";
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "CAD";
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewerComment { get; set; }
    public List<ExpenseItem> Items { get; set; } = new();
    public List<PolicyViolation> Violations { get; set; } = new();
}

public class ExpenseItem
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public DateOnly Date { get; set; }
    public string Merchant { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Uncategorized;
    public bool AutoCategorized { get; set; }
    public string? ReceiptUrl { get; set; }
}

public class PolicyRule
{
    public Guid Id { get; set; }
    public ExpenseCategory Category { get; set; }
    public decimal MaxAmount { get; set; }
    public string Description { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class PolicyViolation
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid? ExpenseItemId { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Remembers which queue messages we already handled so a redelivery does not run twice (idempotency).</summary>
public class ProcessedMessage
{
    public string MessageId { get; set; } = "";
    public string Handler { get; set; } = "";
    public DateTime ProcessedAt { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? Details { get; set; }
    public DateTime At { get; set; }
}
