using TravelXpense.Api.Domain;

namespace TravelXpense.Api.Contracts;

/// <summary>Converts database entities into API response shapes (so we never leak internal fields like password hashes).</summary>
public static class Mappers
{
    public static BookingDto ToDto(this Booking b) =>
        new(b.Id, b.Type, b.Provider, b.Reference, b.Description, b.Price, b.Currency, b.Cabin);

    public static TripDto ToDto(this Trip t) =>
        new(t.Id, t.Destination, t.StartDate, t.EndDate, t.Purpose, t.Status.ToString(),
            t.Bookings.OrderBy(b => b.CreatedAt).Select(b => b.ToDto()).ToList());

    public static ExpenseItemDto ToDto(this ExpenseItem i) =>
        new(i.Id, i.Date, i.Merchant, i.Description, i.Amount, i.Currency, i.Category.ToString(),
            i.AutoCategorized, i.ReceiptUrl);

    public static ReportDto ToDto(this ExpenseReport r) =>
        new(r.Id, r.TripId, r.Title, r.Status.ToString(), r.Total, r.Currency, r.Employee?.FullName ?? "",
            r.CreatedAt, r.SubmittedAt, r.ReviewerComment,
            r.Items.OrderBy(i => i.Date).Select(i => i.ToDto()).ToList(),
            r.Violations.OrderBy(v => v.CreatedAt).Select(v => v.Message).ToList());
}
