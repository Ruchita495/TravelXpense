using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace TravelXpense.Api.Travel;

public record FlightSearchQuery(string Origin, string Destination, DateOnly Date, int Adults = 1);

public record FlightOffer(string Id, string Airline, string Origin, string Destination,
    DateTime Departure, DateTime Arrival, decimal Price, string Currency, string Cabin);

public record FlightSearchResponse(bool CacheHit, List<FlightOffer> Offers);

/// <summary>The "adapter" interface: the rest of the app only knows this, not which vendor is behind it.</summary>
public interface ITravelSearchProvider
{
    Task<IReadOnlyList<FlightOffer>> SearchFlightsAsync(FlightSearchQuery query, CancellationToken ct);
}

/// <summary>Generates repeatable fake flights so the app works with no API keys.</summary>
public class MockTravelProvider : ITravelSearchProvider
{
    private static readonly string[] Airlines = { "Air Canada", "WestJet", "Porter", "United", "Delta" };

    public Task<IReadOnlyList<FlightOffer>> SearchFlightsAsync(FlightSearchQuery q, CancellationToken ct)
    {
        // Same query => same results (stable seed), which keeps demos and tests predictable.
        var seed = $"{q.Origin}{q.Destination}{q.Date:yyyyMMdd}".Aggregate(17, (a, c) => unchecked(a * 31 + c));
        var rng = new Random(seed);
        var offers = new List<FlightOffer>();
        for (var i = 0; i < 6; i++)
        {
            var departure = q.Date.ToDateTime(new TimeOnly(6 + i * 2, rng.Next(0, 4) * 15));
            var arrival = departure.AddMinutes(rng.Next(90, 420));
            var business = i == 5; // one expensive option so you can see a policy violation
            var price = business ? rng.Next(1600, 2800) : rng.Next(180, 900) + 0.99m;
            offers.Add(new FlightOffer($"MOCK-{q.Origin}{q.Destination}-{i}", Airlines[rng.Next(Airlines.Length)],
                q.Origin, q.Destination, departure, arrival, price, "CAD", business ? "BUSINESS" : "ECONOMY"));
        }
        return Task.FromResult<IReadOnlyList<FlightOffer>>(offers.OrderBy(o => o.Price).ToList());
    }
}

/// <summary>Real integration with the Amadeus Self-Service (free test) API.</summary>
public class AmadeusTravelProvider(HttpClient http, IConfiguration cfg) : ITravelSearchProvider
{
    private static string? _token;
    private static DateTime _tokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTime.UtcNow < _tokenExpiry) return _token;
        await Gate.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTime.UtcNow < _tokenExpiry) return _token;

            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/security/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = cfg["Amadeus:ClientId"]!,
                    ["client_secret"] = cfg["Amadeus:ClientSecret"]!
                })
            };
            using var res = await http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            _token = doc.RootElement.GetProperty("access_token").GetString()!;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 60);
            return _token;
        }
        finally { Gate.Release(); }
    }

    public async Task<IReadOnlyList<FlightOffer>> SearchFlightsAsync(FlightSearchQuery q, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var date = q.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = $"v2/shopping/flight-offers?originLocationCode={q.Origin}&destinationLocationCode={q.Destination}" +
                  $"&departureDate={date}&adults={q.Adults}&currencyCode=CAD&max=10";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var offers = new List<FlightOffer>();
        foreach (var o in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var segments = o.GetProperty("itineraries")[0].GetProperty("segments");
            var first = segments[0];
            var last = segments[segments.GetArrayLength() - 1];
            var price = o.GetProperty("price");
            offers.Add(new FlightOffer(
                o.GetProperty("id").GetString()!,
                o.GetProperty("validatingAirlineCodes")[0].GetString() ?? "??",
                q.Origin, q.Destination,
                first.GetProperty("departure").GetProperty("at").GetDateTime(),
                last.GetProperty("arrival").GetProperty("at").GetDateTime(),
                decimal.Parse(price.GetProperty("grandTotal").GetString()!, CultureInfo.InvariantCulture),
                price.GetProperty("currency").GetString() ?? "CAD",
                o.GetProperty("travelerPricings")[0].GetProperty("fareDetailsBySegment")[0].GetProperty("cabin").GetString() ?? "ECONOMY"));
        }
        return offers.OrderBy(o => o.Price).ToList();
    }
}

/// <summary>Adds Redis (or in-memory) caching in front of any provider. Cache failures never break a search.</summary>
public class FlightSearchService(ITravelSearchProvider provider, IDistributedCache cache, ILogger<FlightSearchService> log)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task<FlightSearchResponse> SearchAsync(FlightSearchQuery q, CancellationToken ct = default)
    {
        var key = $"flights:{q.Origin}:{q.Destination}:{q.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{q.Adults}".ToUpperInvariant();

        try
        {
            var cached = await cache.GetStringAsync(key, ct);
            if (cached is not null)
                return new FlightSearchResponse(true, JsonSerializer.Deserialize<List<FlightOffer>>(cached)!);
        }
        catch (Exception ex) { log.LogWarning(ex, "Cache read failed; continuing without cache"); }

        var offers = (await provider.SearchFlightsAsync(q, ct)).ToList();

        try
        {
            await cache.SetStringAsync(key, JsonSerializer.Serialize(offers),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl }, ct);
        }
        catch (Exception ex) { log.LogWarning(ex, "Cache write failed; continuing without cache"); }

        return new FlightSearchResponse(false, offers);
    }
}
