using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelXpense.Api.Contracts;
using TravelXpense.Api.Travel;

namespace TravelXpense.Api.Controllers;

[ApiController, Route("api/flights"), Authorize]
public class FlightsController(FlightSearchService search) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<FlightSearchResponse>> Search([FromQuery] FlightSearchRequest q, CancellationToken ct)
    {
        if (q.Date < DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new ProblemDetails { Title = "Departure date must be today or later." });

        var result = await search.SearchAsync(
            new FlightSearchQuery(q.Origin.ToUpperInvariant(), q.Destination.ToUpperInvariant(), q.Date, q.Adults), ct);
        return Ok(result);
    }
}
