using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

public sealed record AssignmentRun(AssignmentPlan Plan, string OutputPath);

/// <summary>
/// WORKFLOW 5 - courier assignment planning.
/// Input: orders file, couriers file, courier-choice policy, score configuration.
/// Rules: only eligible orders are planned, highest score first; each order needs its native effort points; a courier takes an order
///        only if enough capacity is left; at least one courier must exist.
/// Result: an <see cref="AssignmentPlan"/> (assigned and unassigned orders, load per courier) exported as JSON.
/// Failures (Result): no couriers, native library problem, missing or malformed files, unwritable output. Nothing is exported unless the plan succeeded.
/// </summary>
public static class AssignmentWorkflow
{
    public static Result<AssignmentPlan> Plan(
        ImmutableArray<DeliveryRequest> requests,
        ImmutableArray<Courier> couriers,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> score,
        IEffortCalculator effort,
        Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>> policy,
        string policyName)
    {
        if (requests.IsDefault || couriers.IsDefault)
        {
            return Result.Failure<AssignmentPlan>(ErrorCode.InvalidInput, "The list of requests or couriers is missing.");
        }
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(effort);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policyName);

        return RequireCouriers(couriers)                                                                          // Result<ImmutableArray<Courier>>
            .Bind(available => requests
                .Where(rules.Allows)
                .OrderByDescendingThenById(request => score(request), request => request.Id)                       // extension: dispatch order
                .CollectResults(request => effort.Calculate(request).Map(points => new EffortItem(request, points)))   // native calls, all or nothing
                .Map(items => Assigner.Plan(items, available, policy, policyName)));                                // pure planning
    }

    public static Result<AssignmentRun> Run(
        string? ordersPath,
        string? couriersPath,
        string? outputPath,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> score,
        IEffortCalculator effort,
        Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>> policy,
        string policyName) =>
        DataLoader.LoadRequests(ordersPath)
            .Bind(requests => DataLoader.LoadCouriers(couriersPath)
                .Bind(couriers => Plan(requests, couriers, rules, score, effort, policy, policyName)))
            .Bind(plan => ReportExporter.ExportJson(outputPath, plan.ToExport()).Map(path => new AssignmentRun(plan, path)));

    private static Result<ImmutableArray<Courier>> RequireCouriers(ImmutableArray<Courier> couriers) =>
        couriers.IsEmpty
            ? Result.Failure<ImmutableArray<Courier>>(ErrorCode.InvalidInput, "No couriers are available; add at least one courier before planning.")
            : Result.Success(couriers);
}
