using System.Text;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TravelXpense.Api.Ai;
using TravelXpense.Api.Data;
using TravelXpense.Api.Messaging;
using TravelXpense.Api.Policy;
using TravelXpense.Api.Security;
using TravelXpense.Api.Travel;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ---------- 1. Database (SQL Server / Azure SQL via Entity Framework Core) ----------
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(cfg.GetConnectionString("Default")));

// ---------- 2. Security: JWT login tokens ----------
var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");
if (!builder.Environment.IsDevelopment() && jwt.Key.StartsWith("dev-only"))
    throw new InvalidOperationException("Set a strong Jwt__Key secret (env var / Azure Key Vault) outside development.");

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});
builder.Services.AddAuthorization();

// ---------- 3. Cache: Redis if configured, otherwise in-memory ----------
var redis = cfg["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redis))
    builder.Services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "tx:"; });
else
    builder.Services.AddDistributedMemoryCache();

// ---------- 4. Third-party travel API (Amadeus if keys exist, otherwise mock) ----------
if (!string.IsNullOrWhiteSpace(cfg["Amadeus:ClientId"]))
{
    builder.Services
        .AddHttpClient<ITravelSearchProvider, AmadeusTravelProvider>(c =>
            c.BaseAddress = new Uri(cfg["Amadeus:BaseUrl"] ?? "https://test.api.amadeus.com/"))
        .AddStandardResilienceHandler(); // retries + timeout + circuit breaker for flaky external APIs
}
else
{
    builder.Services.AddSingleton<ITravelSearchProvider, MockTravelProvider>();
}
builder.Services.AddScoped<FlightSearchService>();

// ---------- 5. AI categorization (Claude if key exists, otherwise keyword rules) ----------
builder.Services.AddSingleton<KeywordCategorizer>();
if (!string.IsNullOrWhiteSpace(cfg["Anthropic:ApiKey"]))
{
    builder.Services.AddHttpClient<IExpenseCategorizer, ClaudeCategorizer>(c =>
    {
        c.BaseAddress = new Uri("https://api.anthropic.com/");
        c.DefaultRequestHeaders.Add("x-api-key", cfg["Anthropic:ApiKey"]);
        c.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        c.Timeout = TimeSpan.FromSeconds(15);
    });
}
else
{
    builder.Services.AddSingleton<IExpenseCategorizer>(sp => sp.GetRequiredService<KeywordCategorizer>());
}

// ---------- 6. Messaging: in-memory queue locally, Azure Service Bus in the cloud ----------
if (string.Equals(cfg["Messaging:Provider"], "ServiceBus", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(new ServiceBusClient(cfg["ServiceBus:ConnectionString"]));
    builder.Services.AddSingleton<IEventPublisher, ServiceBusPublisher>();
    builder.Services.AddHostedService<ServiceBusConsumer>();
}
else
{
    builder.Services.AddSingleton<InProcessBus>();
    builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InProcessBus>());
    builder.Services.AddHostedService<InProcessConsumer>();
}
builder.Services.AddSingleton<PolicyService>();
builder.Services.AddScoped<ExpenseEventHandler>();

// ---------- 7. Web API plumbing ----------
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "TravelXpense API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Description = "Log in via /api/auth/login, then paste the token here."
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

var app = builder.Build();

// ---------- Middleware pipeline (order matters!) ----------
app.UseExceptionHandler(); // unexpected errors => clean JSON, never a stack trace
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await DbInitializer.InitializeAsync(app.Services, cfg);
app.Run();

public partial class Program { } // lets the test project start the app in memory
