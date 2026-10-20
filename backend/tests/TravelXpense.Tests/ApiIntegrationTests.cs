using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelXpense.Api.Contracts;
using TravelXpense.Api.Data;

namespace TravelXpense.Tests;

/// <summary>Starts the real API in memory, but swaps SQL Server for an in-memory database.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.Remove(services.Single(s => s.ServiceType == typeof(DbContextOptions<AppDbContext>)));
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
}

public class ApiIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> LoginAsync(string email, string password = "Password123!")
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage res)
    {
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<ReportDto> WaitForProcessingAsync(HttpClient client, Guid reportId)
    {
        for (var i = 0; i < 50; i++) // up to ~5 seconds: the queue consumer works in the background
        {
            var report = await ReadAsync<ReportDto>(await client.GetAsync($"/api/expenses/{reportId}"));
            if (report.Status != "Submitted") return report;
            await Task.Delay(100);
        }
        throw new TimeoutException("Report was not processed in time.");
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var res = await factory.CreateClient().GetAsync("/api/trips");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "employee@demo.com", password = "nope-nope-nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_open_approvals_endpoint()
    {
        var emp = await LoginAsync("employee@demo.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await emp.GetAsync("/api/approvals")).StatusCode);
    }

    [Fact]
    public async Task Registering_same_email_twice_returns_409()
    {
        var client = factory.CreateClient();
        var body = new { email = "dup@example.com", fullName = "Dup User", password = "Password123!" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", body)).StatusCode);
    }

    [Fact]
    public async Task User_cannot_read_another_users_report()
    {
        var emp = await LoginAsync("employee@demo.com");
        var trip = await ReadAsync<TripDto>(await emp.PostAsJsonAsync("/api/trips",
            new { destination = "Montreal", startDate = "2030-03-01", endDate = "2030-03-03", purpose = "Conference" }));
        var report = await ReadAsync<ReportDto>(await emp.PostAsJsonAsync("/api/expenses",
            new { tripId = trip.Id, title = "Private report" }));

        var other = factory.CreateClient();
        var reg = await ReadAsync<AuthResponse>(await other.PostAsJsonAsync("/api/auth/register",
            new { email = "intruder@example.com", fullName = "Intruder", password = "Password123!" }));
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reg.Token);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/expenses/{report.Id}")).StatusCode);
    }

    [Fact]
    public async Task Flight_search_second_call_is_a_cache_hit()
    {
        var emp = await LoginAsync("employee@demo.com");
        var url = "/api/flights/search?origin=YQG&destination=YYZ&date=2030-06-01";
        var first = await ReadAsync<FlightSearchResultDto>(await emp.GetAsync(url));
        var second = await ReadAsync<FlightSearchResultDto>(await emp.GetAsync(url));
        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.NotEmpty(second.Offers);
    }

    [Fact]
    public async Task Full_expense_flow_flags_violations_and_manager_can_approve()
    {
        var emp = await LoginAsync("employee@demo.com");

        var trip = await ReadAsync<TripDto>(await emp.PostAsJsonAsync("/api/trips",
            new { destination = "Toronto", startDate = "2030-01-10", endDate = "2030-01-12", purpose = "Client meeting" }));
        var report = await ReadAsync<ReportDto>(await emp.PostAsJsonAsync("/api/expenses",
            new { tripId = trip.Id, title = "Toronto client visit" }));

        // Coffee: fine. Hotel: over the 300 limit AND no receipt => two violations. Category left blank => auto-categorized.
        await ReadAsync<ReportDto>(await emp.PostAsJsonAsync($"/api/expenses/{report.Id}/items",
            new { date = "2030-01-10", merchant = "Starbucks", description = "Coffee", amount = 6.50, currency = "CAD" }));
        await ReadAsync<ReportDto>(await emp.PostAsJsonAsync($"/api/expenses/{report.Id}/items",
            new { date = "2030-01-10", merchant = "Marriott Downtown", description = "One night", amount = 450, currency = "CAD" }));

        var submit = await emp.PostAsync($"/api/expenses/{report.Id}/submit", null);
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        var processed = await WaitForProcessingAsync(emp, report.Id);
        Assert.Equal("Flagged", processed.Status);
        Assert.True(processed.Violations.Count >= 2);
        Assert.Contains(processed.Items, i => i.Merchant.StartsWith("Marriott") && i.Category == "Lodging" && i.AutoCategorized);

        var mgr = await LoginAsync("manager@demo.com");
        var pending = await ReadAsync<List<ReportDto>>(await mgr.GetAsync("/api/approvals"));
        Assert.Contains(pending, r => r.Id == report.Id);

        var approved = await ReadAsync<ReportDto>(await mgr.PostAsJsonAsync($"/api/approvals/{report.Id}/approve",
            new { comment = "Approved - client dinner covered the receipt gap." }));
        Assert.Equal("Approved", approved.Status);

        // Submitting twice must be rejected (state machine protection)
        Assert.Equal(HttpStatusCode.Conflict, (await emp.PostAsync($"/api/expenses/{report.Id}/submit", null)).StatusCode);
    }
}

// Small local copy of the response shape so the test doesn't depend on the API's internal record.
public record FlightSearchResultDto(bool CacheHit, List<FlightOfferDto> Offers);
public record FlightOfferDto(string Id, string Airline, decimal Price, string Cabin);
