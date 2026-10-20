using TravelXpense.Api.Domain;
using TravelXpense.Api.Policy;

namespace TravelXpense.Tests;

public class PolicyServiceTests
{
    private static readonly PolicyRule[] Rules =
    {
        new() { Id = Guid.NewGuid(), Category = ExpenseCategory.Meals, MaxAmount = 75m, Active = true },
        new() { Id = Guid.NewGuid(), Category = ExpenseCategory.Lodging, MaxAmount = 300m, Active = true },
        new() { Id = Guid.NewGuid(), Category = ExpenseCategory.Other, MaxAmount = 10m, Active = false } // inactive rule must be ignored
    };

    private static ExpenseItem Item(ExpenseCategory c, decimal amount, string? receipt = "https://r.example/1") =>
        new() { Id = Guid.NewGuid(), Merchant = "Test", Category = c, Amount = amount, ReceiptUrl = receipt };

    private readonly PolicyService _sut = new();

    [Fact]
    public void Within_limits_with_receipt_has_no_findings() =>
        Assert.Empty(_sut.Evaluate(new[] { Item(ExpenseCategory.Meals, 40m) }, Rules));

    [Fact]
    public void Amount_over_category_limit_is_flagged()
    {
        var findings = _sut.Evaluate(new[] { Item(ExpenseCategory.Lodging, 450m) }, Rules);
        Assert.Single(findings);
        Assert.Contains("exceeds", findings[0].Message);
    }

    [Fact]
    public void Missing_receipt_over_threshold_is_flagged()
    {
        var findings = _sut.Evaluate(new[] { Item(ExpenseCategory.Meals, 30m, receipt: null) }, Rules);
        Assert.Single(findings);
        Assert.Contains("receipt", findings[0].Message);
    }

    [Fact]
    public void Small_amount_without_receipt_is_allowed() =>
        Assert.Empty(_sut.Evaluate(new[] { Item(ExpenseCategory.Meals, 10m, receipt: null) }, Rules));

    [Fact]
    public void Uncategorized_item_is_flagged() =>
        Assert.Single(_sut.Evaluate(new[] { Item(ExpenseCategory.Uncategorized, 10m) }, Rules));

    [Fact]
    public void Inactive_rules_are_ignored() =>
        Assert.Empty(_sut.Evaluate(new[] { Item(ExpenseCategory.Other, 50m) }, Rules));
}
