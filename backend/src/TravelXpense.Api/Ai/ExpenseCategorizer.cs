using System.Text.Json;
using System.Text.RegularExpressions;
using TravelXpense.Api.Domain;

namespace TravelXpense.Api.Ai;

public interface IExpenseCategorizer
{
    Task<ExpenseCategory> CategorizeAsync(string merchant, string description, CancellationToken ct = default);
}

/// <summary>Free, offline, predictable. Used by default and as a fallback when the AI call fails.</summary>
public class KeywordCategorizer : IExpenseCategorizer
{
    private static readonly (ExpenseCategory Category, string[] Words)[] Map =
    {
        (ExpenseCategory.Airfare, new[] { "air canada", "westjet", "porter", "delta", "united", "flight", "airline", "airways" }),
        (ExpenseCategory.Lodging, new[] { "hotel", "marriott", "hilton", "hyatt", "airbnb", "inn", "resort", "motel" }),
        (ExpenseCategory.GroundTransport, new[] { "uber", "lyft", "taxi", "train", "via rail", "rental", "parking", "fuel", "gas" }),
        (ExpenseCategory.Meals, new[] { "restaurant", "cafe", "coffee", "starbucks", "tim hortons", "mcdonald", "grill", "diner", "lunch", "dinner", "breakfast" })
    };

    public Task<ExpenseCategory> CategorizeAsync(string merchant, string description, CancellationToken ct = default)
    {
        var text = $"{merchant} {description}".ToLowerInvariant();
        foreach (var (category, words) in Map)
        {
            // \b = whole-word match, so "dinner" does not accidentally match "inn".
            if (words.Any(w => Regex.IsMatch(text, $@"\b{Regex.Escape(w)}\b")))
                return Task.FromResult(category);
        }
        return Task.FromResult(ExpenseCategory.Uncategorized);
    }
}

/// <summary>
/// Asks Claude to pick a category. Only merchant + description are sent (no personal data).
/// The reply is validated against our own enum, so a strange answer can never inject data.
/// </summary>
public class ClaudeCategorizer(HttpClient http, KeywordCategorizer fallback, IConfiguration cfg, ILogger<ClaudeCategorizer> log)
    : IExpenseCategorizer
{
    public async Task<ExpenseCategory> CategorizeAsync(string merchant, string description, CancellationToken ct = default)
    {
        try
        {
            var prompt = "Classify this business expense into exactly one category: Airfare, Lodging, Meals, GroundTransport, Other. " +
                         "Reply with only the category name.\n" +
                         $"Merchant: {merchant}\nDescription: {description}";
            var body = new
            {
                model = cfg["Anthropic:Model"] ?? "claude-sonnet-5-5",
                max_tokens = 10,
                messages = new[] { new { role = "user", content = prompt } }
            };

            using var res = await http.PostAsJsonAsync("v1/messages", body, ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()?.Trim();
                if (Enum.TryParse<ExpenseCategory>(text, ignoreCase: true, out var cat)
                    && Enum.IsDefined(cat) && cat != ExpenseCategory.Uncategorized)
                    return cat;
            }
            else
            {
                log.LogWarning("Claude API returned {Status}; using keyword fallback", (int)res.StatusCode);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Claude categorization failed; using keyword fallback");
        }
        return await fallback.CategorizeAsync(merchant, description, ct);
    }
}
