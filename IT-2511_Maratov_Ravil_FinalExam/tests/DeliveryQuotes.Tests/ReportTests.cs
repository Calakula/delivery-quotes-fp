using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member B: the two LINQ reports (pure).
public class ReportTests
{
    private static readonly EligibilityRules Rules = EligibilityRules.Default;
    private static readonly Func<DeliveryRequest, decimal> DistanceFirst = Scoring.CreateScore(ScoreConfig.DistanceFirst);

    private static ImmutableArray<EffortItem> EligibleWithHandPoints() =>
        Deliveries().Where(Rules.Allows).Select(r => new EffortItem(r, HandPoints[r.Id])).ToImmutableArray();

    // REPORT 1: normal result, filtering, projection, ordering, rank
    [Fact]
    public void DispatchList_NormalData_GivesTheDocumentedRanking()
    {
        var report = Reports.DispatchList(Deliveries(), Rules, PricingPolicies.Standard, DistanceFirst);

        Assert.Equal(new[] { "D-05", "D-06", "D-10", "D-02", "D-04", "D-03", "D-01", "D-08" }, report.Select(l => l.Id));   // D-07 and D-09 are filtered out
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, report.Select(l => l.Rank));
        Assert.Equal(new decimal[] { 4350, 2090, 2090, 1800, 1800, 1140, 840, 840 }, report.Select(l => l.Price));
        Assert.Equal(new[] { 64m, 27m, 27m, 22.5m, 22.5m, 9m, 4.5m, 4.5m }, report.Select(l => l.Score));
        Assert.Equal("Dana", report[0].Customer);
    }

    [Fact]
    public void DispatchList_TiedScores_AreOrderedById_WhateverTheInputOrder()
    {
        var forward = Reports.DispatchList(Deliveries(), Rules, PricingPolicies.Standard, DistanceFirst);
        var backward = Reports.DispatchList(Deliveries().Reverse(), Rules, PricingPolicies.Standard, DistanceFirst);

        Assert.Equal(forward.Select(l => l.Id), backward.Select(l => l.Id));
        Assert.Equal(new[] { "D-06", "D-10" }, forward.Where(l => l.Score == 27m).Select(l => l.Id));   // equal score 27: D-06 before D-10
        Assert.Equal(new[] { "D-02", "D-04" }, forward.Where(l => l.Score == 22.5m).Select(l => l.Id));
    }

    [Fact]
    public void DispatchList_EmptyInput_And_NoMatchingRequests_GiveAnEmptyReport()
    {
        var empty = Reports.DispatchList(Array.Empty<DeliveryRequest>(), Rules, PricingPolicies.Standard, DistanceFirst);
        var noMatch = Reports.DispatchList(new[] { Req("D-07", 31m, 2m), Req("D-09", 6m, 25m) }, Rules, PricingPolicies.Standard, DistanceFirst);

        Assert.Empty(empty);
        Assert.Empty(noMatch);
    }

    [Fact]
    public void DispatchList_FilterBoundary_ExactLimitIsIncluded_JustAboveIsNot()
    {
        var report = Reports.DispatchList(
            new[] { Req("A", 30m, 20m), Req("B", 30.01m, 1m), Req("C", 1m, 20.01m) },
            Rules, PricingPolicies.Standard, DistanceFirst);

        Assert.Equal(new[] { "A" }, report.Select(l => l.Id));
    }

    // REPORT 2: grouping, Count, Sum, Aggregate, ordering with a tiebreak
    [Fact]
    public void CustomerWorkload_GroupsAndTotals_MatchTheHandCalculation()
    {
        var report = Reports.CustomerWorkload(EligibleWithHandPoints());

        Assert.Equal(new[] { "Dana", "Aigerim", "Madina", "Nurlan", "Timur" }, report.Select(l => l.Customer));
        Assert.Equal(new[] { 2, 3, 1, 1, 1 }, report.Select(l => l.Orders));
        Assert.Equal(new[] { 40m, 16.5m, 12m, 12m, 2m }, report.Select(l => l.TotalKm));
        Assert.Equal(new[] { 452, 189, 144, 144, 24 }, report.Select(l => l.TotalPoints));   // Dana 120+332, Aigerim 24+120+45
        Assert.Equal(new[] { 8m, 5m, 6m, 6m, 1m }, report.Select(l => l.HeaviestKg));
    }

    [Fact]
    public void CustomerWorkload_TiedTotals_AreOrderedByCustomerName()
    {
        var report = Reports.CustomerWorkload(EligibleWithHandPoints());

        Assert.Equal(144, report[2].TotalPoints);
        Assert.Equal(144, report[3].TotalPoints);
        Assert.Equal("Madina", report[2].Customer);   // Madina < Nurlan
        Assert.Equal("Nurlan", report[3].Customer);
    }

    [Fact]
    public void CustomerWorkload_EmptyInput_AndCaseInsensitiveGrouping()
    {
        Assert.Empty(Reports.CustomerWorkload(Array.Empty<EffortItem>()));

        var merged = Reports.CustomerWorkload(new[]
        {
            new EffortItem(Req("A", 1m, 1m, "dana"), 10),
            new EffortItem(Req("B", 2m, 2m, "Dana"), 20),
        });

        var line = Assert.Single(merged);
        Assert.Equal(2, line.Orders);
        Assert.Equal(30, line.TotalPoints);
        Assert.Equal(2m, line.HeaviestKg);
    }

    [Fact]
    public void Reports_RejectMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Reports.DispatchList(null!, Rules, PricingPolicies.Standard, DistanceFirst));
        Assert.Throws<ArgumentNullException>(() => Reports.CustomerWorkload(null!));
    }
}
