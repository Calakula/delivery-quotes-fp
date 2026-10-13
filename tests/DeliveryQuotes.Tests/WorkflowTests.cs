using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member C: Stage 3 (summary), composed pipeline, application-level scenarios.
public class WorkflowTests
{
    private static readonly string[] SampleLines =
    {
        "Id,Customer,DistanceKm,WeightKg",
        "R-001,Aigerim,2,1",
        "R-002,Dana,10,5",
        "r-003,Nurlan,30,8",
        "R-004,Aruzhan,31,2",
        "R-005,Timur,4.5,0",
        "R-006,Madina,6,25",
    };

    private static ImmutableArray<DeliveryRequest> ParseValid(IEnumerable<string> lines)
    {
        var report = RequestParser.ParseLines(lines);
        Assert.True(report.IsValid, string.Join("; ", report.Errors));
        return report.Requests;
    }

    private static DeliveryRequest Req(string id, decimal km, decimal kg) => new(id, "Test", km, kg);

    [Fact]
    public void NormalWorkflow_StandardPolicy_ProducesDocumentedQuotes()
    {
        var summary = DeliveryPipeline.Run(ParseValid(SampleLines), EligibilityRules.Default, PricingPolicies.Standard);

        Assert.Equal(new[] { "R-003", "R-002", "R-005", "R-001" }, summary.Quotes.Select(q => q.Id));
        Assert.Equal(new decimal[] { 4350, 1800, 1140, 840 }, summary.Quotes.Select(q => q.Price));
        Assert.Equal(8130m, summary.TotalPrice);
        Assert.Equal(2, summary.SkippedCount); // R-004 (31 km) and R-006 (25 kg)
    }

    [Fact]
    public void NormalWorkflow_SampleFileFromDataFolder_ProducesSameResult()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "data", "requests.csv"));
        var summary = DeliveryPipeline.Run(ParseValid(lines), EligibilityRules.Default, PricingPolicies.Express);

        Assert.Equal(12520m, summary.TotalPrice);
        Assert.Equal(4, summary.Quotes.Length);
    }

    [Fact]
    public void EmptyDataset_HeaderOnly_IsValidAndGivesEmptySummary()
    {
        var report = RequestParser.ParseLines(new[] { RequestParser.Header });
        Assert.True(report.IsValid);
        Assert.Empty(report.Requests);

        var summary = DeliveryPipeline.Run(report.Requests, EligibilityRules.Default, PricingPolicies.Standard);

        Assert.Empty(summary.Quotes);
        Assert.Equal(0m, summary.TotalPrice);
        Assert.Equal(0, summary.SkippedCount);
    }

    [Fact]
    public void TwoPolicies_SameInput_DifferInDocumentedWay()
    {
        var input = ParseValid(SampleLines);

        var standard = DeliveryPipeline.Run(input, EligibilityRules.Default, PricingPolicies.Standard);
        var express = DeliveryPipeline.Run(input, EligibilityRules.Default, PricingPolicies.Express);

        Assert.Equal(840m, standard.Quotes.Single(q => q.Id == "R-001").Price);
        Assert.Equal(1360m, express.Quotes.Single(q => q.Id == "R-001").Price);
        Assert.Equal(8130m, standard.TotalPrice);
        Assert.Equal(12520m, express.TotalPrice);
        Assert.Equal(standard.Quotes.Select(q => q.Id), express.Quotes.Select(q => q.Id)); // same pipeline, same selection
    }

    [Fact]
    public void MissingCollectionOrPolicy_IsRejected_NoFabricatedResult()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryPipeline.Run(default, EligibilityRules.Default, PricingPolicies.Standard));
        Assert.Throws<ArgumentNullException>(() =>
            DeliveryPipeline.Run(ImmutableArray<DeliveryRequest>.Empty, EligibilityRules.Default, null!));
        Assert.Throws<ArgumentException>(() => PipelineStages.BuildSummary(default, 0));
    }

    [Fact]
    public void Stage3_OrdersByPriceThenId_AndKeepsInputUnchanged()
    {
        var priced = ImmutableArray.Create(
            new PricedRequest(Req("B", 2m, 1m), 840m),
            new PricedRequest(Req("A", 2m, 1m), 840m),
            new PricedRequest(Req("C", 9m, 1m), 1680m));
        var snapshot = priced.ToArray();

        var summary = PipelineStages.BuildSummary(priced, skippedCount: 3);

        Assert.Equal(new[] { "C", "A", "B" }, summary.Quotes.Select(q => q.Id)); // price desc, ties by Id
        Assert.Equal(3360m, summary.TotalPrice);
        Assert.Equal(3, summary.SkippedCount);
        Assert.Equal(snapshot, priced.ToArray());                                // caller's collection untouched
    }
}
