using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

public static partial class PipelineStages
{
    /// <summary>
    /// STAGE 1 - select.
    /// In : ImmutableArray&lt;DeliveryRequest&gt; (already validated and normalised by the factory) + EligibilityRules
    /// Out: ImmutableArray&lt;DeliveryRequest&gt; (only requests within limits)
    /// Rule: rules.Allows(request): DistanceKm &lt;= MaxDistanceKm AND WeightKg &lt;= MaxWeightKg (inclusive).
    /// Example: (R-003, 30 km, 8 kg) kept; (R-004, 31 km) dropped.
    /// </summary>
    public static ImmutableArray<DeliveryRequest> SelectEligible(
        ImmutableArray<DeliveryRequest> requests,
        EligibilityRules rules)
    {
        RequireInitialized(requests, nameof(requests));
        ArgumentNullException.ThrowIfNull(rules);

        return requests.Where(rules.Allows).ToImmutableArray();
    }
}
