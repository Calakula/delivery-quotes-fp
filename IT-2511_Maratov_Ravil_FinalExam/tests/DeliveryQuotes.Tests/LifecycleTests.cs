using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member C: the real application workflow (file -> factory -> order book -> lifecycle).
public class LifecycleTests
{
    private static ImmutableArray<DeliveryRequest> SampleRequests()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "data", "requests.csv"));
        var report = RequestParser.ParseLines(lines);
        Assert.True(report.IsValid, string.Join("; ", report.Errors));
        return report.Requests;
    }

    [Fact]
    public void SampleFile_StandardPolicy_ConfirmsEligibleOrdersAndRefusesTheRest()
    {
        var report = OrderProcessor.Run(SampleRequests(), EligibilityRules.Default, PricingPolicies.Standard, "standard");

        Assert.Equal(6, report.Book.Count);
        Assert.Equal(19, report.Steps.Length); // 6 submit + 6 quote + 6 confirm + 1 repeated confirm

        var expected = new (string Id, decimal Price, int Number)[] { ("R-001", 840m, 1), ("R-002", 1800m, 2), ("R-003", 4350m, 3), ("R-005", 1140m, 4) };
        foreach (var e in expected)
        {
            Assert.True(report.Book.TryGet(e.Id, out var state));
            var confirmed = Assert.IsType<OrderState.Confirmed>(state);
            Assert.Equal(e.Price, confirmed.Price);
            Assert.Equal(e.Number, confirmed.ConfirmationNumber);
        }

        foreach (var id in new[] { "R-004", "R-006" })
        {
            Assert.True(report.Book.TryGet(id, out var state));
            Assert.IsType<OrderState.Submitted>(state);
        }

        Assert.Equal(4, report.Steps.Count(s => s.Result is TransitionResult.Refused)); // quote and confirm of R-004 and R-006
    }

    [Fact]
    public void RepeatedConfirmationStep_ReturnsTheExistingConfirmation()
    {
        var report = OrderProcessor.Run(SampleRequests(), EligibilityRules.Default, PricingPolicies.Standard, "standard");

        var repeat = report.Steps.Single(s => s.Action == "Confirm (repeat)");

        var moved = Assert.IsType<TransitionResult.Moved>(repeat.Result);
        Assert.Equal(1, Assert.IsType<OrderState.Confirmed>(moved.Next).ConfirmationNumber); // still #1, not a new number
    }

    [Fact]
    public void TwoPolicies_SameLifecycle_DifferOnlyInPrices()
    {
        var standard = OrderProcessor.Run(SampleRequests(), EligibilityRules.Default, PricingPolicies.Standard, "standard");
        var express = OrderProcessor.Run(SampleRequests(), EligibilityRules.Default, PricingPolicies.Express, "express");

        Assert.True(standard.Book.TryGet("R-003", out var s));
        Assert.True(express.Book.TryGet("R-003", out var x));
        Assert.Equal(4350m, Assert.IsType<OrderState.Confirmed>(s).Price);
        Assert.Equal(6550m, Assert.IsType<OrderState.Confirmed>(x).Price);
        Assert.Equal(standard.Steps.Length, express.Steps.Length);
    }

    [Fact]
    public void EmptyDataset_GivesEmptyBookAndNoSteps()
    {
        var report = OrderProcessor.Run(ImmutableArray<DeliveryRequest>.Empty, EligibilityRules.Default, PricingPolicies.Standard, "standard");

        Assert.Equal(0, report.Book.Count);
        Assert.Empty(report.Steps);
    }

    [Fact]
    public void DuplicateIdsInInput_SecondSubmitIsRefused_OnlyOneOrderExists()
    {
        var input = ImmutableArray.Create(Req("R-1", 2m, 1m, "First"), Req("r-1", 3m, 1m, "Second"));

        var report = OrderProcessor.Run(input, EligibilityRules.Default, PricingPolicies.Standard, "standard");

        Assert.Equal(1, report.Book.Count);
        Assert.IsType<TransitionResult.Refused>(report.Steps.Where(s => s.Action == "Submit").ElementAt(1).Result);
        Assert.True(report.Book.TryGet("R-1", out var state));
        Assert.Equal("First", Assert.IsType<OrderState.Confirmed>(state).Request.Customer);
    }

    [Fact]
    public void MissingInput_IsRejectedByTheProcessor()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderProcessor.Run(default, EligibilityRules.Default, PricingPolicies.Standard, "standard"));
        Assert.Throws<ArgumentNullException>(() =>
            OrderProcessor.Run(ImmutableArray<DeliveryRequest>.Empty, EligibilityRules.Default, null!, "standard"));
    }
}
