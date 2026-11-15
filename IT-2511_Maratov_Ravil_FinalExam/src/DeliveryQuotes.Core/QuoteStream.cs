namespace DeliveryQuotes.Core;

/// <summary>A request together with its price.</summary>
public sealed record PricedLine(DeliveryRequest Request, decimal Price);

/// <summary>
/// Lazy (deferred) workflow built with yield return.
/// </summary>
public static class QuoteStream
{
    /// <summary>
    /// Prices requests one by one, on demand. Arguments are checked immediately, but the body of the iterator does NOT run
    /// when this method is called: the source is read and the policy is invoked only while the result is being enumerated,
    /// and only for as many items as the consumer asks for. Every new enumeration starts again and calls the policy again;
    /// call ToArray / ToImmutableArray once to keep a stable snapshot that can be reused without recalculation.
    /// The method has no side effects: it neither changes the source nor business state, and it holds no resource.
    /// </summary>
    public static IEnumerable<PricedLine> PriceLines(IEnumerable<DeliveryRequest> requests, Func<DeliveryRequest, decimal> policy)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(policy);

        return Iterate(requests, policy);
    }

    private static IEnumerable<PricedLine> Iterate(IEnumerable<DeliveryRequest> requests, Func<DeliveryRequest, decimal> policy)
    {
        foreach (var request in requests)
        {
            yield return new PricedLine(request, policy(request));
        }
    }
}
