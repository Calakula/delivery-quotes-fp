using System.Diagnostics.CodeAnalysis;

namespace DeliveryQuotes.Core;

/// <summary>
/// Two interchangeable pricing policies for the same rule "how much does this delivery cost".
/// Both are pure functions with the shape Func&lt;DeliveryRequest, decimal&gt;.
/// </summary>
public static class PricingPolicies
{
    public const decimal FreeWeightKg = 5m;          // up to and including 5 kg: no surcharge
    public const decimal SurchargePerExtraKg = 50m;  // each kg above 5 kg

    /// <summary>600 + 120 per km (+ weight surcharge).</summary>
    public static decimal Standard(DeliveryRequest request) => Price(request, baseFee: 600m, perKm: 120m);

    /// <summary>1000 + 180 per km (+ weight surcharge).</summary>
    public static decimal Express(DeliveryRequest request) => Price(request, baseFee: 1000m, perKm: 180m);

    /// <summary>Used by the application layer to select a policy by name, outside the pipeline.</summary>
    public static bool TryGetByName(string? name, [NotNullWhen(true)] out Func<DeliveryRequest, decimal>? policy)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "standard":
                policy = Standard;
                return true;
            case "express":
                policy = Express;
                return true;
            default:
                policy = null;
                return false;
        }
    }

    private static decimal Price(DeliveryRequest request, decimal baseFee, decimal perKm)
    {
        var extraKg = Math.Max(0m, request.WeightKg - FreeWeightKg);
        var raw = baseFee + perKm * request.DistanceKm + extraKg * SurchargePerExtraKg;
        return Math.Round(raw, 0, MidpointRounding.AwayFromZero);
    }
}
