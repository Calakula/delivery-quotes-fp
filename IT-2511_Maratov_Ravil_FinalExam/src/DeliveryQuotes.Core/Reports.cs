using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>One row of the dispatch list.</summary>
public sealed record DispatchLine(int Rank, string Id, string Customer, decimal DistanceKm, decimal WeightKg, decimal Price, decimal Score);

/// <summary>A request with its native effort points (input of the workload report).</summary>
public sealed record EffortItem(DeliveryRequest Request, int Points);

/// <summary>One row of the customer workload report.</summary>
public sealed record WorkloadLine(string Customer, int Orders, decimal TotalKm, int TotalPoints, decimal HeaviestKg);

/// <summary>
/// The two LINQ reports. Pure: they read only their arguments, never touch the console, files or the native library.
/// </summary>
public static class Reports
{
    /// <summary>
    /// REPORT 1 - Dispatch list.
    /// Intended user: the dispatcher, to decide which deliveries go out first.
    /// Input: validated requests (loaded from the orders file), a price policy and a configured score function.
    /// Filter: only requests within the service limits.
    /// Output fields: Rank, Id, Customer, DistanceKm, WeightKg, Price, Score.
    /// Stages: Where (eligible) -> lazy yield-return pricing -> Select (add score) -> OrderByDescending + ThenBy (score, then id)
    ///         -> Select with index (rank) -> ToImmutableArray (the one materialisation point).
    /// Expected: higher score first; equal scores ordered by id; empty input or no eligible request gives an empty list.
    /// </summary>
    public static ImmutableArray<DispatchLine> DispatchList(
        IEnumerable<DeliveryRequest> requests,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        Func<DeliveryRequest, decimal> score)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(score);

        return QuoteStream.PriceLines(requests.Where(rules.Allows), policy)
            .Select(line => (Line: line, Score: score(line.Request)))
            .OrderByDescendingThenById(x => x.Score, x => x.Line.Request.Id)
            .Select((x, index) => new DispatchLine(
                index + 1,
                x.Line.Request.Id,
                x.Line.Request.Customer,
                x.Line.Request.DistanceKm,
                x.Line.Request.WeightKg,
                x.Line.Price,
                x.Score))
            .ToImmutableArray();
    }

    /// <summary>
    /// REPORT 2 - Customer workload.
    /// Intended user: the planner, to see how much courier effort each customer needs.
    /// Input: requests together with their effort points (the points come from the native library, calculated before this report).
    /// Filter: none here (the caller passes the eligible requests).
    /// Output fields: Customer, Orders (Count), TotalKm (Sum), TotalPoints (Sum), HeaviestKg (Aggregate).
    /// Stages: GroupBy customer (case-insensitive) -> Select (Count, Sum, Aggregate) -> OrderByDescending + ThenBy (points, then name).
    /// Expected: the customer with most points first; equal points ordered by customer name; empty input gives an empty report.
    /// </summary>
    public static ImmutableArray<WorkloadLine> CustomerWorkload(IEnumerable<EffortItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .GroupBy(item => item.Request.Customer, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkloadLine(
                group.Key,
                group.Count(),
                group.Sum(item => item.Request.DistanceKm),
                group.Sum(item => item.Points),
                group.Aggregate(0m, (heaviest, item) => Math.Max(heaviest, item.Request.WeightKg))))
            .OrderByDescendingThenById(line => line.TotalPoints, line => line.Customer)
            .ToImmutableArray();
    }
}
