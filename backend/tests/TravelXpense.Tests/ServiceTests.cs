using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelXpense.Api.Ai;
using TravelXpense.Api.Domain;
using TravelXpense.Api.Travel;

namespace TravelXpense.Tests;

public class KeywordCategorizerTests
{
    private readonly KeywordCategorizer _sut = new();

    [Theory]
    [InlineData("Uber", "Ride to airport", ExpenseCategory.GroundTransport)]
    [InlineData("Marriott Downtown", "", ExpenseCategory.Lodging)]
    [InlineData("Air Canada", "Flight YQG-YYZ", ExpenseCategory.Airfare)]
    [InlineData("Joe's Grill", "Dinner with client", ExpenseCategory.Meals)]
    [InlineData("Acme Stationery", "Pens", ExpenseCategory.Uncategorized)]
    public async Task Categorizes_common_merchants(string merchant, string description, ExpenseCategory expected) =>
        Assert.Equal(expected, await _sut.CategorizeAsync(merchant, description));
}

public class FlightSearchServiceTests
{
    private sealed class CountingProvider : ITravelSearchProvider
    {
        public int Calls;
        public Task<IReadOnlyList<FlightOffer>> SearchFlightsAsync(FlightSearchQuery q, CancellationToken ct)
        {
            Calls++;
            IReadOnlyList<FlightOffer> list = new List<FlightOffer>
            {
                new("1", "Test Air", q.Origin, q.Destination, DateTime.UtcNow, DateTime.UtcNow.AddHours(2), 199m, "CAD", "ECONOMY")
            };
            return Task.FromResult(list);
        }
    }

    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task Second_identical_search_is_served_from_cache()
    {
        var provider = new CountingProvider();
        var sut = new FlightSearchService(provider, NewCache(), NullLogger<FlightSearchService>.Instance);
        var query = new FlightSearchQuery("YQG", "YYZ", new DateOnly(2030, 1, 10));

        var first = await sut.SearchAsync(query);
        var second = await sut.SearchAsync(query);

        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.Equal(1, provider.Calls); // the "vendor" was only called once
    }

    [Fact]
    public async Task Different_dates_are_cached_separately()
    {
        var provider = new CountingProvider();
        var sut = new FlightSearchService(provider, NewCache(), NullLogger<FlightSearchService>.Instance);

        await sut.SearchAsync(new FlightSearchQuery("YQG", "YYZ", new DateOnly(2030, 1, 10)));
        await sut.SearchAsync(new FlightSearchQuery("YQG", "YYZ", new DateOnly(2030, 1, 11)));

        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Mock_provider_returns_repeatable_results()
    {
        var mock = new MockTravelProvider();
        var q = new FlightSearchQuery("YQG", "YYZ", new DateOnly(2030, 1, 10));
        var a = await mock.SearchFlightsAsync(q, default);
        var b = await mock.SearchFlightsAsync(q, default);
        Assert.Equal(a.Select(o => o.Price), b.Select(o => o.Price));
    }
}
