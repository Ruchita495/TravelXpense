using System.ComponentModel.DataAnnotations;
using TravelXpense.Api.Domain;

namespace TravelXpense.Api.Contracts;

// ===== Requests (what the client sends us). Data annotations = automatic input validation. =====
public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MaxLength(200)] public string FullName { get; set; } = "";
    [Required, MinLength(8), MaxLength(100)] public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MaxLength(100)] public string Password { get; set; } = "";
}

public class CreateTripRequest
{
    [Required, MaxLength(200)] public string Destination { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [Required, MaxLength(500)] public string Purpose { get; set; } = "";
}

public class FlightSearchRequest
{
    [Required, StringLength(3, MinimumLength = 3)] public string Origin { get; set; } = "";
    [Required, StringLength(3, MinimumLength = 3)] public string Destination { get; set; } = "";
    public DateOnly Date { get; set; }
    [Range(1, 9)] public int Adults { get; set; } = 1;
}

public class BookFlightRequest
{
    [Required, MaxLength(100)] public string OfferId { get; set; } = "";
    [Required, StringLength(3, MinimumLength = 3)] public string Origin { get; set; } = "";
    [Required, StringLength(3, MinimumLength = 3)] public string Destination { get; set; } = "";
    public DateOnly Date { get; set; }
}

public class CreateReportRequest
{
    public Guid TripId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
}

public class AddExpenseItemRequest
{
    public DateOnly Date { get; set; }
    [Required, MaxLength(200)] public string Merchant { get; set; } = "";
    [MaxLength(500)] public string Description { get; set; } = "";
    [Range(0.01, 100000)] public decimal Amount { get; set; }
    [StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "CAD";
    public ExpenseCategory? Category { get; set; }
    [MaxLength(500), Url] public string? ReceiptUrl { get; set; }
}

public class ReviewRequest
{
    [MaxLength(1000)] public string? Comment { get; set; }
}

// ===== Responses (what we send back). Records = short, immutable data holders. =====
public record AuthResponse(string Token, string Email, string FullName, string Role);

public record BookingDto(Guid Id, string Type, string Provider, string Reference, string Description,
    decimal Price, string Currency, string Cabin);

public record TripDto(Guid Id, string Destination, DateOnly StartDate, DateOnly EndDate, string Purpose,
    string Status, List<BookingDto> Bookings);

public record ExpenseItemDto(Guid Id, DateOnly Date, string Merchant, string Description, decimal Amount,
    string Currency, string Category, bool AutoCategorized, string? ReceiptUrl);

public record ReportDto(Guid Id, Guid TripId, string Title, string Status, decimal Total, string Currency,
    string EmployeeName, DateTime CreatedAt, DateTime? SubmittedAt, string? ReviewerComment,
    List<ExpenseItemDto> Items, List<string> Violations);
