using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

public static partial class PipelineStages
{
    /// <summary>
    /// STAGE 2 - compute the price (higher-order: calls the supplied policy delegate once per request).
    /// In : ImmutableArray&lt;DeliveryRequest&gt; + Func&lt;DeliveryRequest, decimal&gt;
    /// Out: ImmutableArray&lt;PricedRequest&gt;
    /// Example: Standard policy, (2 km, 1 kg) -> price 840.
    /// </summary>
    public static ImmutableArray<PricedRequest> PriceRequests(
        ImmutableArray<DeliveryRequest> eligible,
        Func<DeliveryRequest, decimal> policy)
    {
        RequireInitialized(eligible, nameof(eligible));
        ArgumentNullException.ThrowIfNull(policy);

        return eligible
            .Select(r => new PricedRequest(r, CheckedPrice(r, policy(r))))
            .ToImmutableArray();
    }

    private static decimal CheckedPrice(DeliveryRequest request, decimal price) =>
        price >= 0m
            ? price
            : throw new InvalidOperationException($"Pricing policy returned a negative price for {request.Id}.");
}
