using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member B: deferred execution (yield return) and deliberate materialisation. The counters are test probes only.
public class LazyProcessingTests
{
    private static IEnumerable<DeliveryRequest> ProbedSource(IEnumerable<DeliveryRequest> items, Action onRead)
    {
        foreach (var item in items)
        {
            onRead();
            yield return item;
        }
    }

    [Fact]
    public void PriceLines_CreatingTheEnumerable_RunsNothing_ButChecksArgumentsAtOnce()
    {
        var sourceReads = 0;
        var policyCalls = 0;
        decimal Policy(DeliveryRequest r)
        {
            policyCalls++;
            return 1m;
        }

        var lazy = QuoteStream.PriceLines(ProbedSource(Deliveries(), () => sourceReads++), Policy);

        Assert.Equal(0, sourceReads);    // no processing before enumeration
        Assert.Equal(0, policyCalls);
        Assert.Throws<ArgumentNullException>(() => QuoteStream.PriceLines(null!, Policy));   // argument errors are reported immediately
        Assert.Throws<ArgumentNullException>(() => QuoteStream.PriceLines(Deliveries(), null!));
        Assert.NotNull(lazy);
    }

    [Fact]
    public void PriceLines_Iteration_TriggersProcessing_AndStopsWhenTheConsumerStops()
    {
        var sourceReads = 0;
        var policyCalls = 0;
        decimal Policy(DeliveryRequest r)
        {
            policyCalls++;
            return 7m;
        }

        var lazy = QuoteStream.PriceLines(ProbedSource(Deliveries(), () => sourceReads++), Policy);
        var firstTwo = lazy.Take(2).ToArray();

        Assert.Equal(2, firstTwo.Length);
        Assert.Equal(new[] { "D-01", "D-02" }, firstTwo.Select(l => l.Request.Id));
        Assert.Equal(2, sourceReads);    // only the items that were asked for were read and priced
        Assert.Equal(2, policyCalls);
        Assert.Equal(7m, firstTwo[0].Price);
    }

    [Fact]
    public void Materialisation_GivesAStableSnapshot_WhileRepeatedLazyEnumerationRepeatsTheWork()
    {
        var policyCalls = 0;
        decimal Policy(DeliveryRequest r)
        {
            policyCalls++;
            return 5m;
        }
        var requests = Deliveries();   // 10 requests

        var lazy = QuoteStream.PriceLines(requests, Policy);
        _ = lazy.ToArray();
        _ = lazy.ToArray();
        Assert.Equal(20, policyCalls);       // before materialisation: every enumeration repeats the calculation

        policyCalls = 0;
        var snapshot = lazy.ToArray();       // deliberate materialisation point
        Assert.Equal(10, policyCalls);
        _ = snapshot.Sum(l => l.Price);
        _ = snapshot.Count();
        _ = snapshot.Select(l => l.Request.Id).ToList();
        Assert.Equal(10, policyCalls);       // after materialisation: reuse costs nothing and the values are stable
    }

    [Fact]
    public void DispatchList_ProcessesEachEligibleRequestOnce_AndItsResultCanBeReused()
    {
        var policyCalls = 0;
        var scoreCalls = 0;
        decimal Policy(DeliveryRequest r)
        {
            policyCalls++;
            return 100m;
        }
        decimal Score(DeliveryRequest r)
        {
            scoreCalls++;
            return r.DistanceKm;
        }

        var report = Reports.DispatchList(Deliveries(), EligibilityRules.Default, Policy, Score);
        _ = report.Select(l => l.Id).ToArray();
        _ = report.Sum(l => l.Price);

        Assert.Equal(8, report.Length);
        Assert.Equal(8, policyCalls);   // eight eligible requests, each priced exactly once
        Assert.Equal(8, scoreCalls);
    }
}
