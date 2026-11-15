using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member A: CreateScore, currying and partial application.
public class ScoringTests
{
    [Fact]
    public void TwoConfiguredFunctions_SameInput_GiveTheDocumentedDifferentScores()
    {
        var distanceFirst = Scoring.CreateScore(ScoreConfig.DistanceFirst);   // 2 per km, 0.5 per kg
        var heavyFirst = Scoring.CreateScore(ScoreConfig.HeavyFirst);          // 0.5 per km, 2 per kg
        var small = Req("R-001", 2m, 1m);       // 2 km, 1 kg
        var long30 = Req("R-003", 30m, 8m);     // 30 km, 8 kg

        Assert.Equal(4.5m, distanceFirst(small));    // 2*2 + 1*0.5
        Assert.Equal(3m, heavyFirst(small));         // 2*0.5 + 1*2
        Assert.Equal(64m, distanceFirst(long30));    // 30*2 + 8*0.5
        Assert.Equal(31m, heavyFirst(long30));       // 30*0.5 + 8*2
        Assert.NotEqual(distanceFirst(small), heavyFirst(small));
    }

    [Fact]
    public void ConfiguredFunction_EqualsTheDirectCall_AndConfigurationsDoNotInterfere()
    {
        var request = Req("R-002", 10m, 5m);

        var first = Scoring.CreateScore(new ScoreConfig(1m, 1m));
        var before = first(request);
        var second = Scoring.CreateScore(new ScoreConfig(10m, 10m));   // creating another function changes nothing global

        Assert.Equal(Scoring.WeightedScore(1m, 1m, request), before);
        Assert.Equal(15m, first(request));
        Assert.Equal(150m, second(request));
        Assert.Equal(before, first(request));
    }

    [Fact]
    public void ConfiguredFunctions_ChangeTheRankingOfTheSameDataset()
    {
        var rules = EligibilityRules.Default;

        var byDistance = Reports.DispatchList(Deliveries(), rules, PricingPolicies.Standard, Scoring.CreateScore(ScoreConfig.DistanceFirst));
        var byWeight = Reports.DispatchList(Deliveries(), rules, PricingPolicies.Standard, Scoring.CreateScore(ScoreConfig.HeavyFirst));

        Assert.Equal(new[] { "D-05", "D-06", "D-10", "D-02", "D-04", "D-03", "D-01", "D-08" }, byDistance.Select(l => l.Id));
        Assert.Equal(new[] { "D-05", "D-06", "D-10", "D-02", "D-04", "D-01", "D-08", "D-03" }, byWeight.Select(l => l.Id));
        Assert.Equal(6, byDistance.Single(l => l.Id == "D-03").Rank);   // D-03 (4.5 km, 0 kg) is sixth by distance ...
        Assert.Equal(8, byWeight.Single(l => l.Id == "D-03").Rank);     // ... and last when weight counts more
    }

    [Fact]
    public void ScoreConfig_Find_PresentAndAbsent()
    {
        Assert.Equal(ScoreConfig.DistanceFirst, Assert.IsType<Option<ScoreConfig>.Some>(ScoreConfig.Find(" Distance-First ")).Value);
        Assert.Equal(ScoreConfig.HeavyFirst, Assert.IsType<Option<ScoreConfig>.Some>(ScoreConfig.Find("heavy-first")).Value);
        Assert.IsType<Option<ScoreConfig>.None>(ScoreConfig.Find("other"));
        Assert.IsType<Option<ScoreConfig>.None>(ScoreConfig.Find(null));
    }
}
