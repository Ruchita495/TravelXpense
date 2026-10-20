using TravelXpense.Api.Domain;

namespace TravelXpense.Api.Policy;

public record PolicyFinding(Guid? ItemId, string Message);

/// <summary>
/// Pure business logic (no database, no HTTP) so it is trivial to unit test.
/// Checks each expense line against the company's spending rules.
/// </summary>
public class PolicyService
{
    public const decimal ReceiptRequiredAbove = 25m;

    public IReadOnlyList<PolicyFinding> Evaluate(IEnumerable<ExpenseItem> items, IEnumerable<PolicyRule> rules)
    {
        var limits = rules.Where(r => r.Active)
            .GroupBy(r => r.Category)
            .ToDictionary(g => g.Key, g => g.Min(r => r.MaxAmount));

        var findings = new List<PolicyFinding>();
        foreach (var item in items)
        {
            if (item.Category == ExpenseCategory.Uncategorized)
            {
                findings.Add(new(item.Id, $"'{item.Merchant}': category could not be determined."));
            }
            else if (limits.TryGetValue(item.Category, out var max) && item.Amount > max)
            {
                findings.Add(new(item.Id, $"'{item.Merchant}': {item.Amount:0.00} exceeds the {item.Category} limit of {max:0.00}."));
            }

            if (item.Amount > ReceiptRequiredAbove && string.IsNullOrWhiteSpace(item.ReceiptUrl))
            {
                findings.Add(new(item.Id, $"'{item.Merchant}': a receipt is required for amounts over {ReceiptRequiredAbove:0.00}."));
            }
        }
        return findings;
    }
}
