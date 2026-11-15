using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>Outcome of a complete run: the reports and the file they were exported to.</summary>
public sealed record ReportRun(ReportBundle Bundle, string OutputPath);

/// <summary>
/// The integration layer: it composes the boundaries (file loader, native adapter, exporter) with the pure parts
/// (Assignment 5 pipeline, the two reports). Nothing is exported unless every earlier step succeeded.
/// </summary>
public static class ReportingWorkflow
{
    /// <summary>Builds both reports and the Assignment 5 summary. No console, no file access; the native calls go through <paramref name="effort"/>.</summary>
    public static Result<ReportBundle> Build(
        ImmutableArray<DeliveryRequest> requests,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        Func<DeliveryRequest, decimal> score,
        IEffortCalculator effort)
    {
        if (requests.IsDefault)
        {
            return Result.Failure<ReportBundle>(ErrorCode.InvalidInput, "The list of requests is missing.");
        }
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(effort);

        var summary = DeliveryPipeline.Run(requests, rules, policy);                   // Assignment 5 pipeline
        var dispatch = Reports.DispatchList(requests, rules, policy, score);            // report 1

        return requests
            .Where(rules.Allows)
            .CollectResults(request => effort.Calculate(request).Map(points => new EffortItem(request, points)))   // native calls, all or nothing
            .Map(items => new ReportBundle(summary, dispatch, Reports.CustomerWorkload(items)));                  // report 2
    }

    /// <summary>File to reports to file: load (boundary), build (pure + native adapter), export (boundary).</summary>
    public static Result<ReportRun> Run(
        string? ordersPath,
        string? outputPath,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        Func<DeliveryRequest, decimal> score,
        IEffortCalculator effort) =>
        DataLoader.LoadRequests(ordersPath)
            .Bind(requests => Build(requests, rules, policy, score, effort))
            .Bind(bundle => ReportExporter.Export(outputPath, bundle).Map(path => new ReportRun(bundle, path)));
}
