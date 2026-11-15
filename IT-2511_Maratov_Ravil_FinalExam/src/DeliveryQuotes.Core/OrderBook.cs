using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace DeliveryQuotes.Core;

/// <summary>
/// Immutable store of orders by id. Every operation returns a NEW book together with the outcome.
/// A refused operation returns the SAME book instance, so a failure can never leave a half-applied update.
/// All prerequisites (order exists, transition allowed) are checked BEFORE the new book is created.
/// </summary>
public sealed class OrderBook
{
    private readonly ImmutableDictionary<string, OrderState> _orders;

    private OrderBook(ImmutableDictionary<string, OrderState> orders)
    {
        _orders = orders;
    }

    public static OrderBook Empty { get; } = new(ImmutableDictionary<string, OrderState>.Empty);

    public int Count => _orders.Count;

    /// <summary>Order ids in ordinal order.</summary>
    public ImmutableArray<string> Ids => _orders.Keys.OrderBy(k => k, StringComparer.Ordinal).ToImmutableArray();

    public bool TryGet(string? id, [NotNullWhen(true)] out OrderState? state) =>
        _orders.TryGetValue(DeliveryRequest.NormalizeId(id), out state);

    /// <summary>Lookup: Some(state) when the order exists, None when it does not (an empty or null id is simply not found).</summary>
    public Option<OrderState> Find(string? id) =>
        TryGet(id, out var state) ? Option.Some(state) : Option.None<OrderState>();

    /// <summary>Accepts an already validated request. A duplicate id is refused (documented rule).</summary>
    public (OrderBook Book, TransitionResult Result) Submit(DeliveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_orders.ContainsKey(request.Id))
        {
            return (this, TransitionResult.Refuse(ErrorCode.DuplicateOrder, $"Order {request.Id} already exists; a duplicate submission is refused."));
        }

        var submitted = new OrderState.Submitted(request);
        return (new OrderBook(_orders.Add(request.Id, submitted)), new TransitionResult.Moved(submitted));
    }

    /// <summary>Accepts the factory outcome; Invalid never enters the book.</summary>
    public (OrderBook Book, TransitionResult Result) Submit(RequestValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        switch (validation)
        {
            case RequestValidation.Valid valid:
                return Submit(valid.Request);
            case RequestValidation.Invalid invalid:
                return (this, new TransitionResult.Refused(ErrorCode.InvalidRequest, invalid.Reasons));
            default:
                return (this, TransitionResult.Refuse(ErrorCode.Unexpected, "Unknown validation outcome; submission refused."));
        }
    }

    public (OrderBook Book, TransitionResult Result) Quote(
        string? id,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        string policyName) =>
        Apply(id, state => OrderWorkflow.Quote(state, rules, policy, policyName));

    public (OrderBook Book, TransitionResult Result) Confirm(string? id, int confirmationNumber) =>
        Apply(id, state => OrderWorkflow.Confirm(state, confirmationNumber));

    private (OrderBook Book, TransitionResult Result) Apply(string? id, Func<OrderState, TransitionResult> transition)
    {
        var key = DeliveryRequest.NormalizeId(id);

        if (!_orders.TryGetValue(key, out var current))
        {
            return (this, TransitionResult.Refuse(ErrorCode.NotFound, $"Order '{(id ?? string.Empty).Trim()}' does not exist; submit it first."));
        }

        var result = transition(current);

        if (result is TransitionResult.Moved moved)
        {
            return (new OrderBook(_orders.SetItem(key, moved.Next)), moved);
        }

        return (this, result); // refused (or unexpected): nothing is stored
    }
}
