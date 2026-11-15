using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuotes.Core;

/// <summary>The state of one courier while a plan is being built: the courier and the points already given.</summary>
public sealed record CourierLoad(Courier Courier, int Used)
{
    public int Remaining => Courier.Capacity - Used;
}

/// <summary>
/// The two mutually exclusive outcomes for one order, each with its own payload:
///   Assigned   - the order, the courier and the capacity the courier has left;
///   Unassigned - the order and the reason why no courier took it.
/// </summary>
public abstract record AssignmentOutcome
{
    private AssignmentOutcome()
    {
    }

    public sealed record Assigned(EffortItem Order, Courier Courier, int RemainingCapacity) : AssignmentOutcome;

    public sealed record Unassigned(EffortItem Order, string Reason) : AssignmentOutcome;
}

/// <summary>Summary line per courier.</summary>
public sealed record CourierLoadLine(string CourierId, string Name, int Capacity, int Used, int Orders);

public sealed record AssignedOrder(string OrderId, string Customer, int Points, string CourierId, int RemainingCapacity);

public sealed record UnassignedOrder(string OrderId, string Customer, int Points, string Reason);

/// <summary>Plain data of a plan, for JSON export (the polymorphic outcomes are flattened here by pattern matching).</summary>
public sealed record AssignmentExport(string Policy, ImmutableArray<AssignedOrder> Assigned, ImmutableArray<UnassignedOrder> Unassigned, ImmutableArray<CourierLoadLine> Loads);

/// <summary>The complete result of the planning, in dispatch order.</summary>
public sealed record AssignmentPlan(string PolicyName, ImmutableArray<AssignmentOutcome> Outcomes, ImmutableArray<CourierLoadLine> Loads)
{
    public AssignmentExport ToExport()
    {
        var assigned = ImmutableArray.CreateBuilder<AssignedOrder>();
        var unassigned = ImmutableArray.CreateBuilder<UnassignedOrder>();

        foreach (var outcome in Outcomes)
        {
            switch (outcome)
            {
                case AssignmentOutcome.Assigned a:
                    assigned.Add(new AssignedOrder(a.Order.Request.Id, a.Order.Request.Customer, a.Order.Points, a.Courier.Id, a.RemainingCapacity));
                    break;
                case AssignmentOutcome.Unassigned u:
                    unassigned.Add(new UnassignedOrder(u.Order.Request.Id, u.Order.Request.Customer, u.Order.Points, u.Reason));
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled assignment outcome {outcome.GetType().Name}.");
            }
        }

        return new AssignmentExport(PolicyName, assigned.ToImmutable(), unassigned.ToImmutable(), Loads);
    }
}

/// <summary>
/// Two interchangeable courier-choice policies for the same business rule "which courier takes this order".
/// A policy receives the current loads and the points needed, and returns the chosen load or None.
/// </summary>
public static class AssignmentPolicies
{
    /// <summary>The first courier, in the given order, that still has enough capacity.</summary>
    public static Option<CourierLoad> FirstFit(ImmutableArray<CourierLoad> loads, int points)
    {
        foreach (var load in loads)
        {
            if (load.Remaining >= points)
            {
                return Option.Some(load);
            }
        }

        return Option.None<CourierLoad>();
    }

    /// <summary>Among couriers that can take the order, the one with the least capacity left (ties by courier id), so large gaps stay free.</summary>
    public static Option<CourierLoad> BestFit(ImmutableArray<CourierLoad> loads, int points)
    {
        var fitting = loads.Where(load => load.Remaining >= points).ToArray();
        return fitting.Length == 0
            ? Option.None<CourierLoad>()
            : Option.Some(fitting.OrderBy(load => load.Remaining).ThenBy(load => load.Courier.Id, StringComparer.Ordinal).First());
    }

    public static Option<Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>>> Find(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "first-fit" => Option.Some<Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>>>(FirstFit),
        "best-fit" => Option.Some<Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>>>(BestFit),
        _ => Option.None<Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>>>(),
    };
}

/// <summary>
/// The pure planning step. Higher-order: the courier choice is the delegate <c>policy</c>, which is called for every order.
/// The orders are processed in the given order with Aggregate; every step creates a new state, the inputs are never changed.
/// </summary>
public static class Assigner
{
    private sealed record PlanState(ImmutableArray<CourierLoad> Loads, ImmutableArray<AssignmentOutcome> Outcomes);

    public static AssignmentPlan Plan(
        IEnumerable<EffortItem> orderedOrders,
        IEnumerable<Courier> couriers,
        Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>> policy,
        string policyName)
    {
        ArgumentNullException.ThrowIfNull(orderedOrders);
        ArgumentNullException.ThrowIfNull(couriers);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policyName);

        var start = new PlanState(couriers.Select(c => new CourierLoad(c, 0)).ToImmutableArray(), ImmutableArray<AssignmentOutcome>.Empty);
        var end = orderedOrders.Aggregate(start, (state, order) => Step(state, order, policy));

        var lines = end.Loads
            .Select(load => new CourierLoadLine(
                load.Courier.Id,
                load.Courier.Name,
                load.Courier.Capacity,
                load.Used,
                end.Outcomes.OfType<AssignmentOutcome.Assigned>().Count(a => a.Courier.Id == load.Courier.Id)))
            .ToImmutableArray();

        return new AssignmentPlan(policyName, end.Outcomes, lines);
    }

    private static PlanState Step(PlanState state, EffortItem order, Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>> policy)
    {
        var choice = policy(state.Loads, order.Points);

        switch (choice)
        {
            case Option<CourierLoad>.Some some when some.Value.Remaining >= order.Points:
            {
                var chosen = some.Value;
                var loads = state.Loads
                    .Select(load => load.Courier.Id == chosen.Courier.Id ? load with { Used = load.Used + order.Points } : load)
                    .ToImmutableArray();
                var outcome = new AssignmentOutcome.Assigned(order, chosen.Courier, chosen.Remaining - order.Points);
                return new PlanState(loads, state.Outcomes.Add(outcome));
            }

            case Option<CourierLoad>.Some:
                return Reject(state, order, "the policy chose a courier without enough capacity left");

            default:
                return Reject(state, order, ReasonFor(state.Loads, order.Points));
        }
    }

    private static PlanState Reject(PlanState state, EffortItem order, string reason) =>
        new(state.Loads, state.Outcomes.Add(new AssignmentOutcome.Unassigned(order, reason)));

    private static string ReasonFor(ImmutableArray<CourierLoad> loads, int points)
    {
        var largest = loads.Select(load => load.Courier.Capacity).DefaultIfEmpty(0).Max();
        return points > largest
            ? string.Create(CultureInfo.InvariantCulture, $"needs {points} points, more than the largest courier capacity ({largest})")
            : string.Create(CultureInfo.InvariantCulture, $"no courier has {points} points of capacity left");
    }
}
