using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

public static partial class PipelineStages
{
    /// <summary>
    /// STAGE 1 - normalise and select.
    /// In : ImmutableArray&lt;DeliveryRequest&gt; + EligibilityRules
    /// Out: ImmutableArray&lt;DeliveryRequest&gt; (Id upper-cased, names trimmed, only requests within limits)
    /// Rule: DistanceKm &lt;= MaxDistanceKm AND WeightKg &lt;= MaxWeightKg (limits are inclusive).
    /// Example: ("r-003", 30 km, 8 kg) -> ("R-003", 30 km, 8 kg) kept; (31 km) dropped.
    /// </summary>
    public static ImmutableArray<DeliveryRequest> SelectEligible(
        ImmutableArray<DeliveryRequest> requests,
        EligibilityRules rules)
    {
        RequireInitialized(requests, nameof(requests));
        ArgumentNullException.ThrowIfNull(rules);

        return requests
            .Select(Normalise)
            .Where(r => r.DistanceKm <= rules.MaxDistanceKm && r.WeightKg <= rules.MaxWeightKg)
            .ToImmutableArray();
    }

    private static DeliveryRequest Normalise(DeliveryRequest request) =>
        request with { Id = request.Id.Trim().ToUpperInvariant(), Customer = request.Customer.Trim() };
}
