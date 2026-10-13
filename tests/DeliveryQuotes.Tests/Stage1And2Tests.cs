using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member B: Stage 1 (select), Stage 2 (price with policy delegate), pricing policies.
public class Stage1And2Tests
{
    private static DeliveryRequest Req(string id, decimal km, decimal kg) => new(id, "Test", km, kg);

    [Fact]
    public void Boundary_ExactLimitsAreIncluded_JustAboveIsExcluded()
    {
        var requests = ImmutableArray.Create(
            Req("A", 30m, 20m),      // exactly on both limits -> kept
            Req("B", 30.01m, 1m),    // just over distance -> dropped
            Req("C", 1m, 20.01m));   // just over weight -> dropped

        var eligible = PipelineStages.SelectEligible(requests, EligibilityRules.Default);

        Assert.Equal(new[] { "A" }, eligible.Select(r => r.Id));
    }

    [Fact]
    public void Boundary_FiveKgHasNoSurcharge_AboveFiveKgIsCharged()
    {
        // Standard: 600 + 120 * 1 km = 720
        Assert.Equal(720m, PricingPolicies.Standard(Req("A", 1m, 5m)));   // exactly 5 kg: free
        Assert.Equal(745m, PricingPolicies.Standard(Req("B", 1m, 5.5m))); // 0.5 kg * 50 = 25
        Assert.Equal(720m, PricingPolicies.Standard(Req("C", 1m, 0m)));   // zero weight is valid
    }

    [Fact]
    public void TwoPolicies_SameRequest_GiveDifferentDocumentedPrices()
    {
        var request = Req("R-001", 2m, 1m);

        Assert.Equal(840m, PricingPolicies.Standard(request));   // 600 + 2 * 120
        Assert.Equal(1360m, PricingPolicies.Express(request));   // 1000 + 2 * 180
    }

    [Fact]
    public void HigherOrderStage_InvokesPolicyDelegate_OncePerEligibleRequest()
    {
        var calls = 0;
        decimal CountingPolicy(DeliveryRequest r)
        {
            calls++;
            return 100m;
        }

        var eligible = ImmutableArray.Create(Req("A", 1m, 1m), Req("B", 2m, 2m), Req("C", 3m, 3m));
        var priced = PipelineStages.PriceRequests(eligible, CountingPolicy);

        Assert.Equal(3, calls);
        Assert.All(priced, p => Assert.Equal(100m, p.Price));
    }

    [Fact]
    public void ThresholdChange_UsesSuppliedRules_NotHiddenConstants()
    {
        var requests = ImmutableArray.Create(Req("A", 25m, 1m), Req("B", 10m, 1m));

        var narrow = PipelineStages.SelectEligible(requests, new EligibilityRules(20m, 20m));

        Assert.Equal(new[] { "B" }, narrow.Select(r => r.Id));
    }

    [Fact]
    public void Stage1_DoesNotChangeCallerData_AndReturnsNormalisedCopies()
    {
        var original = ImmutableArray.Create(
            new DeliveryRequest("r-003", "  Nurlan ", 30m, 8m),
            new DeliveryRequest("R-004", "Aruzhan", 31m, 2m));
        var snapshot = original.ToArray();

        var eligible = PipelineStages.SelectEligible(original, EligibilityRules.Default);

        Assert.Equal(snapshot, original.ToArray());          // caller's records and collection untouched
        Assert.Equal("r-003", original[0].Id);
        Assert.Equal("R-003", eligible.Single().Id);          // result holds a normalised copy
        Assert.Equal("Nurlan", eligible.Single().Customer);
        Assert.NotSame(original[0], eligible[0]);
    }

    [Fact]
    public void Stage2_IsDeterministic_AndKeepsInputUnchanged()
    {
        var eligible = ImmutableArray.Create(Req("B", 2m, 1m), Req("A", 2m, 1m));
        var snapshot = eligible.ToArray();

        var priced1 = PipelineStages.PriceRequests(eligible, PricingPolicies.Standard);
        var priced2 = PipelineStages.PriceRequests(eligible, PricingPolicies.Standard);

        Assert.Equal(priced1.ToArray(), priced2.ToArray());
        Assert.Equal(snapshot, eligible.ToArray());
        Assert.Equal(new[] { "B", "A" }, priced1.Select(p => p.Request.Id)); // input order not rearranged
    }

    [Fact]
    public void NegativePriceFromPolicy_IsRejected()
    {
        var eligible = ImmutableArray.Create(Req("A", 1m, 1m));

        Assert.Throws<InvalidOperationException>(() => PipelineStages.PriceRequests(eligible, _ => -1m));
    }

    [Theory]
    [InlineData("standard", true)]
    [InlineData(" EXPRESS ", true)]
    [InlineData("cheap", false)]
    [InlineData(null, false)]
    public void PolicyLookup_ByName(string? name, bool expected)
    {
        Assert.Equal(expected, PricingPolicies.TryGetByName(name, out var policy));
        Assert.Equal(expected, policy is not null);
    }
}
