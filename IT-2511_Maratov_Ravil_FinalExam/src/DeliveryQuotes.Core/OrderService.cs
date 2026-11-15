using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuotes.Core;

public enum ProcessAction
{
    Quote,    // Submitted -> Quoted
    Confirm,  // Quoted -> Confirmed (an order that was never quoted is refused)
    Full,     // Quote, then Confirm
}

/// <summary>Display model of an order.</summary>
public sealed record OrderView(string Id, string Customer, string Status, string Text);

/// <summary>New book (only on success) and the display model of the resulting state.</summary>
public sealed record ProcessOutcome(OrderBook Book, OrderView View);

/// <summary>
/// Result chain B on the order book: find (required lookup) -> quote -> confirm -> display model.
/// A missing order stops everything; a refused quote stops the confirmation. On failure no new book exists,
/// so the caller's book is exactly as before.
/// </summary>
public static class OrderService
{
    private sealed record Working(OrderBook Book, OrderState State);

    /// <summary>Builds a book from validated requests; a duplicate id fails the whole load (no partial book).</summary>
    public static Result<OrderBook> LoadBook(ImmutableArray<DeliveryRequest> requests)
    {
        if (requests.IsDefault)
        {
            return Result.Failure<OrderBook>(ErrorCode.InvalidInput, "The list of requests is missing.");
        }

        var book = OrderBook.Empty;
        foreach (var request in requests)
        {
            var (next, outcome) = book.Submit(request);
            if (outcome is TransitionResult.Refused refused)
            {
                return Result.Failure<OrderBook>(refused.Code, refused.Message);
            }
            book = next;
        }

        return Result.Success(book);
    }

    public static Result<ProcessOutcome> Process(
        OrderBook book,
        string? orderId,
        ProcessAction action,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        string policyName,
        DiscountTable discounts,
        int confirmationNumber)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policyName);
        ArgumentNullException.ThrowIfNull(discounts);

        return FindOrder(book, orderId)
            .Bind(w => action is ProcessAction.Quote or ProcessAction.Full
                ? QuoteStep(w, rules, policy, policyName, discounts)
                : Result.Success(w))
            .Bind(w => action is ProcessAction.Confirm or ProcessAction.Full
                ? ConfirmStep(w, confirmationNumber)
                : Result.Success(w))
            .Map(w => new ProcessOutcome(w.Book, ToView(w.State)));
    }

    // Required lookup: Option -> Result (NotFound).
    private static Result<Working> FindOrder(OrderBook book, string? orderId) =>
        book.Find(orderId)
            .Map(state => new Working(book, state))
            .ToResult(() => new Error(ErrorCode.NotFound, $"Order '{(orderId ?? string.Empty).Trim()}' does not exist; submit it first."));

    private static Result<Working> QuoteStep(Working w, EligibilityRules rules, Func<DeliveryRequest, decimal> policy, string policyName, DiscountTable discounts)
    {
        var request = OrderWorkflow.RequestOf(w.State);
        var percent = discounts.Find(request.Customer).GetValueOr(0m);
        var name = percent > 0m ? string.Create(CultureInfo.InvariantCulture, $"{policyName}, -{percent:0.##}%") : policyName;

        var (book, outcome) = w.Book.Quote(request.Id, rules, PricingPolicies.WithDiscount(policy, percent), name);
        return outcome.ToResult().Map(next => new Working(book, next));
    }

    private static Result<Working> ConfirmStep(Working w, int confirmationNumber)
    {
        var (book, outcome) = w.Book.Confirm(OrderWorkflow.RequestOf(w.State).Id, confirmationNumber);
        return outcome.ToResult().Map(next => new Working(book, next));
    }

    private static OrderView ToView(OrderState state)
    {
        var request = OrderWorkflow.RequestOf(state);
        var status = state switch
        {
            OrderState.Submitted => "Submitted",
            OrderState.Quoted => "Quoted",
            OrderState.Confirmed => "Confirmed",
            _ => "Unknown",
        };
        return new OrderView(request.Id, request.Customer, status, OrderWorkflow.Describe(state));
    }
}
