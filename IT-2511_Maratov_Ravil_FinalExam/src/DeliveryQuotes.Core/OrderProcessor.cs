using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>One executed step of the lifecycle: what was attempted, for which order, and the outcome.</summary>
public sealed record Step(string Action, string OrderId, TransitionResult Result);

/// <summary>Final book and the full list of steps.</summary>
public sealed record ProcessingReport(OrderBook Book, ImmutableArray<Step> Steps);

/// <summary>
/// The real application workflow for one dataset: submit every validated request, quote all, confirm all,
/// then repeat one confirmation. Pure: all inputs are arguments, the result is a value.
/// Orders that cannot be quoted (outside the limits) stay Submitted, and their confirmation is refused
/// because the prerequisite (a quote) is missing.
/// </summary>
public static class OrderProcessor
{
    public static ProcessingReport Run(
        ImmutableArray<DeliveryRequest> requests,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        string policyName)
    {
        if (requests.IsDefault)
        {
            throw new ArgumentException("Input collection is missing (default ImmutableArray).", nameof(requests));
        }
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policyName);

        var book = OrderBook.Empty;
        var steps = ImmutableArray.CreateBuilder<Step>();

        foreach (var request in requests)
        {
            var (next, result) = book.Submit(request);
            book = next;
            steps.Add(new Step("Submit", request.Id, result));
        }

        foreach (var id in book.Ids)
        {
            var (next, result) = book.Quote(id, rules, policy, policyName);
            book = next;
            steps.Add(new Step("Quote", id, result));
        }

        var confirmed = 0;
        foreach (var id in book.Ids)
        {
            var (next, result) = book.Confirm(id, confirmed + 1);
            book = next;
            steps.Add(new Step("Confirm", id, result));
            if (result is TransitionResult.Moved)
            {
                confirmed++;
            }
        }

        foreach (var id in book.Ids)
        {
            if (book.TryGet(id, out var state) && state is OrderState.Confirmed)
            {
                var (next, result) = book.Confirm(id, confirmed + 1);
                book = next;
                steps.Add(new Step("Confirm (repeat)", id, result));
                break;
            }
        }

        return new ProcessingReport(book, steps.ToImmutable());
    }
}
