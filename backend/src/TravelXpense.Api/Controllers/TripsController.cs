using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelXpense.Api.Contracts;
using TravelXpense.Api.Data;
using TravelXpense.Api.Domain;
using TravelXpense.Api.Security;
using TravelXpense.Api.Travel;

namespace TravelXpense.Api.Controllers;

[ApiController, Route("api/trips"), Authorize]
public class TripsController(AppDbContext db, FlightSearchService flights) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TripDto>>> List()
    {
        var uid = User.GetUserId();
        var trips = await db.Trips.Include(t => t.Bookings)
            .Where(t => t.EmployeeId == uid)               // users only ever see their OWN trips
            .OrderByDescending(t => t.CreatedAt).ToListAsync();
        return trips.Select(t => t.ToDto()).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<TripDto>> Create(CreateTripRequest req)
    {
        if (req.EndDate < req.StartDate)
            return BadRequest(new ProblemDetails { Title = "End date must be on or after the start date." });

        var trip = new Trip
        {
            Id = Guid.NewGuid(), EmployeeId = User.GetUserId(), Destination = req.Destination.Trim(),
            StartDate = req.StartDate, EndDate = req.EndDate, Purpose = req.Purpose.Trim(), CreatedAt = DateTime.UtcNow
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return Ok(trip.ToDto());
    }

    [HttpPost("{id:guid}/bookings")]
    public async Task<ActionResult<TripDto>> BookFlight(Guid id, BookFlightRequest req, CancellationToken ct)
    {
        var uid = User.GetUserId();
        var trip = await db.Trips.Include(t => t.Bookings).FirstOrDefaultAsync(t => t.Id == id && t.EmployeeId == uid, ct);
        if (trip is null) return NotFound();

        // Never trust a price sent by the browser: look the offer up again on the server.
        var results = await flights.SearchAsync(
            new FlightSearchQuery(req.Origin.ToUpperInvariant(), req.Destination.ToUpperInvariant(), req.Date), ct);
        var offer = results.Offers.FirstOrDefault(o => o.Id == req.OfferId);
        if (offer is null)
            return BadRequest(new ProblemDetails { Title = "That offer is no longer available. Please search again." });

        var booking = new Booking
        {
            Id = Guid.NewGuid(), TripId = trip.Id, Type = "Flight", Provider = offer.Airline,
            Reference = "TX" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), // simulated booking reference (PNR)
            Description = $"{offer.Origin} to {offer.Destination}, departs {offer.Departure:yyyy-MM-dd HH:mm}",
            Price = offer.Price, Currency = offer.Currency, Cabin = offer.Cabin, CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);
        return Ok(trip.ToDto());
    }
}
