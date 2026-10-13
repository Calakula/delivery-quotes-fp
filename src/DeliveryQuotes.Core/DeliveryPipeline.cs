using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>The visible workflow: Stage 1 -> Stage 2 -> Stage 3.</summary>
public static class DeliveryPipeline
{
    /// <summary>
    /// Same requests + same rules + same policy always give the same summary.
    /// The policy is chosen by the caller and only passed in; it is never read from global state.
    /// </summary>
    public static QuoteSummary Run(
        ImmutableArray<DeliveryRequest> requests,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy)
    {
        var eligible = PipelineStages.SelectEligible(requests, rules);   // Stage 1
        var priced = PipelineStages.PriceRequests(eligible, policy);      // Stage 2
        return PipelineStages.BuildSummary(priced, requests.Length - eligible.Length); // Stage 3
    }
}
