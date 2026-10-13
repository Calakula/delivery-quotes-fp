using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

public static partial class PipelineStages
{
    /// <summary>
    /// STAGE 3 - build the result model.
    /// In : ImmutableArray&lt;PricedRequest&gt; + number of skipped requests
    /// Out: QuoteSummary (quotes by price descending, ties by Id; total price; skipped count)
    /// Example: [R-001:840, R-002:1800] -> quotes [R-002, R-001], total 2640.
    /// </summary>
    public static QuoteSummary BuildSummary(ImmutableArray<PricedRequest> priced, int skippedCount)
    {
        RequireInitialized(priced, nameof(priced));
        ArgumentOutOfRangeException.ThrowIfNegative(skippedCount);

        var quotes = priced
            .OrderByDescending(p => p.Price)
            .ThenBy(p => p.Request.Id, StringComparer.Ordinal)
            .Select(p => new Quote(p.Request.Id, p.Request.Customer, p.Price))
            .ToImmutableArray();

        return new QuoteSummary(quotes, quotes.Sum(q => q.Price), skippedCount);
    }
}
