using System.Collections.Immutable;
using System.Globalization;
using DeliveryQuotes.Core;

// Impure shell: arguments, console output and the single place where a Result is turned into an exit code.
// All decisions and calculations are in DeliveryQuotes.Core; all file access is in DataLoader.
//
// Usage:
//   <csv> <standard|express|both>                                   quote summary (Assignment 5 pipeline)
//   lifecycle <csv> <standard|express>                              submit -> quote -> confirm (Assignment 6 states)
//   quote <policy> <id> <customer> <km> <kg> [discounts.json]       one quote typed by the user (Assignment 7 chain A)
//   process <csv> <policy> <orderId> <quote|confirm|full> [discounts.json]   one order of the file (chain B)
//   reports <csv> <policy> <distance-first|heavy-first> <outputFile>   two LINQ reports + native call + JSON export
//   assign <csv> <couriers.json> <first-fit|best-fit> <distance-first|heavy-first> <outputFile>   courier assignment plan (final project)
//
// Exit codes: 0 ok | 1 file missing, unreadable or not writable | 2 malformed file content | 3 domain failure
//             4 native library problem | 64 bad arguments | 70 unexpected case

return args switch
{
    ["lifecycle", var csv, var policy] => RunLifecycle(csv, policy),
    ["quote", var policy, var id, var customer, var km, var kg] => RunQuote(policy, id, customer, km, kg, null),
    ["quote", var policy, var id, var customer, var km, var kg, var discounts] => RunQuote(policy, id, customer, km, kg, discounts),
    ["process", var csv, var policy, var orderId, var action] => RunProcess(csv, policy, orderId, action, null),
    ["process", var csv, var policy, var orderId, var action, var discounts] => RunProcess(csv, policy, orderId, action, discounts),
    ["reports", var csv, var policy, var config, var output] => RunReports(csv, policy, config, output),
    ["assign", var csv, var couriers, var policy, var config, var output] => RunAssign(csv, couriers, policy, config, output),
    [var csv, var policy] => RunSummary(csv, policy),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Usage: <csv> <standard|express|both>");
    Console.Error.WriteLine("       lifecycle <csv> <standard|express>");
    Console.Error.WriteLine("       quote <standard|express> <id> <customer> <km> <kg> [discounts.json]");
    Console.Error.WriteLine("       process <csv> <standard|express> <orderId> <quote|confirm|full> [discounts.json]");
    Console.Error.WriteLine("       reports <csv> <standard|express> <distance-first|heavy-first> <outputFile>");
    Console.Error.WriteLine("       assign <csv> <couriers.json> <first-fit|best-fit> <distance-first|heavy-first> <outputFile>");
    return 64;
}

// The one place where the final outcome is handled, with explicit pattern matching.
static int Report<T>(Result<T> result, Action<T> onSuccess) where T : notnull
{
    switch (result)
    {
        case Result<T>.Ok ok:
            onSuccess(ok.Value);
            return 0;
        case Result<T>.Failure failure:
            Console.Error.WriteLine($"{Describe(failure.Error.Code)} [{failure.Error.Code}]: {failure.Error.Message}");
            return ExitCodeFor(failure.Error.Code);
        default:
            Console.Error.WriteLine("Unexpected result case.");
            return 70;
    }
}

static string Describe(ErrorCode code) => code switch
{
    ErrorCode.FileNotFound or ErrorCode.FileUnreadable or ErrorCode.MalformedContent or ErrorCode.FileNotWritable => "File problem",
    ErrorCode.NativeLibraryUnavailable or ErrorCode.NativeCallFailed => "Native library problem",
    ErrorCode.Unexpected => "Internal problem",
    _ => "Rejected",
};

static int ExitCodeFor(ErrorCode code) => code switch
{
    ErrorCode.FileNotFound or ErrorCode.FileUnreadable or ErrorCode.FileNotWritable => 1,
    ErrorCode.NativeLibraryUnavailable or ErrorCode.NativeCallFailed => 4,
    ErrorCode.MalformedContent => 2,
    ErrorCode.Unexpected => 70,
    _ => 3,
};

static Result<PolicyChoice> ResolvePolicy(string name)
{
    var normalized = name.Trim().ToLowerInvariant();
    return PricingPolicies.Find(normalized)
        .Map(policy => new PolicyChoice(normalized, policy))
        .ToResult(() => new Error(ErrorCode.NotFound, $"Policy '{name}' was not found. Use standard or express."));
}

static Result<DiscountTable> LoadDiscountsOrNone(string? path) =>
    path is null ? Result.Success(DiscountTable.Empty) : DataLoader.LoadDiscounts(path);

static Result<ScoreConfig> ResolveScoreConfig(string name) =>
    ScoreConfig.Find(name).ToResult(() => new Error(ErrorCode.NotFound, $"Score configuration '{name}' was not found. Use distance-first or heavy-first."));

static Result<ProcessAction> ParseAction(string text) => text.Trim().ToLowerInvariant() switch
{
    "quote" => Result.Success(ProcessAction.Quote),
    "confirm" => Result.Success(ProcessAction.Confirm),
    "full" => Result.Success(ProcessAction.Full),
    _ => Result.Failure<ProcessAction>(ErrorCode.InvalidInput, $"Action '{text}' is not known. Use quote, confirm or full."),
};

static int RunSummary(string csv, string mode)
{
    var wanted = mode.Trim().ToLowerInvariant();
    var names = wanted == "both" ? new[] { "standard", "express" } : new[] { wanted };

    var choices = ImmutableArray.CreateBuilder<PolicyChoice>();
    foreach (var name in names)
    {
        switch (ResolvePolicy(name))
        {
            case Result<PolicyChoice>.Ok ok:
                choices.Add(ok.Value);
                break;
            case Result<PolicyChoice>.Failure failure:
                Console.Error.WriteLine($"{failure.Error.Message}");
                return 64;
        }
    }

    return Report(DataLoader.LoadRequests(csv), requests =>
    {
        var totals = new List<decimal>();
        foreach (var choice in choices)
        {
            var summary = DeliveryPipeline.Run(requests, EligibilityRules.Default, choice.Policy);
            Console.WriteLine($"Policy: {choice.Name}");
            Console.WriteLine($"Quotes: {summary.Quotes.Length} (not eligible: {summary.SkippedCount})");
            foreach (var q in summary.Quotes)
            {
                Console.WriteLine($"  {q.Id,-6} {q.Customer,-10} {q.Price,7:0} KZT");
            }
            Console.WriteLine($"Total: {summary.TotalPrice:0} KZT");
            Console.WriteLine();
            totals.Add(summary.TotalPrice);
        }

        if (totals.Count == 2)
        {
            Console.WriteLine($"Express minus standard total: {totals[1] - totals[0]:0} KZT");
        }
    });
}

static int RunLifecycle(string csv, string policyName)
{
    var result = ResolvePolicy(policyName).Bind(choice =>
        DataLoader.LoadRequests(csv).Map(requests =>
            (Choice: choice, Report: OrderProcessor.Run(requests, EligibilityRules.Default, choice.Policy, choice.Name))));

    return Report(result, outcome =>
    {
        Console.WriteLine($"Lifecycle, policy: {outcome.Choice.Name}");
        foreach (var step in outcome.Report.Steps)
        {
            var text = step.Result switch
            {
                TransitionResult.Moved moved => $"OK       {OrderWorkflow.Describe(moved.Next)}",
                TransitionResult.Refused refused => $"REFUSED  {refused.Message}",
                _ => "UNKNOWN RESULT",
            };
            Console.WriteLine($"  {step.Action,-16} {step.OrderId,-6} {text}");
        }

        Console.WriteLine();
        Console.WriteLine("Final orders:");
        foreach (var id in outcome.Report.Book.Ids)
        {
            if (outcome.Report.Book.TryGet(id, out var state))
            {
                Console.WriteLine($"  {OrderWorkflow.Describe(state)}");
            }
        }
    });
}

static int RunQuote(string policyName, string id, string customer, string km, string kg, string? discountsPath)
{
    var result = ResolvePolicy(policyName).Bind(choice =>
        LoadDiscountsOrNone(discountsPath).Bind(discounts =>
            QuoteWorkflow.QuoteOne(id, customer, km, kg, EligibilityRules.Default, choice.Policy, choice.Name, discounts)));

    return Report(result, quote =>
    {
        var discountText = quote.DiscountRecordFound
            ? string.Create(CultureInfo.InvariantCulture, $"discount {quote.DiscountPercent:0.##}% from the customer record")
            : "no discount record";
        Console.WriteLine($"Quote {quote.Id} for {quote.Customer}: {quote.Price:0} KZT ({quote.PolicyName}, {discountText})");
    });
}

static int RunProcess(string csv, string policyName, string orderId, string actionText, string? discountsPath)
{
    var result = ResolvePolicy(policyName).Bind(choice =>
        ParseAction(actionText).Bind(action =>
            DataLoader.LoadRequests(csv).Bind(OrderService.LoadBook).Bind(book =>
                LoadDiscountsOrNone(discountsPath).Bind(discounts =>
                    OrderService.Process(book, orderId, action, EligibilityRules.Default, choice.Policy, choice.Name, discounts, 1)))));

    return Report(result, outcome =>
    {
        Console.WriteLine($"{outcome.View.Status}: {outcome.View.Text}");
    });
}

static Result<AssignmentPolicyChoice> ResolveAssignmentPolicy(string name)
{
    var normalized = name.Trim().ToLowerInvariant();
    return AssignmentPolicies.Find(normalized)
        .Map(policy => new AssignmentPolicyChoice(normalized, policy))
        .ToResult(() => new Error(ErrorCode.NotFound, $"Assignment policy '{name}' was not found. Use first-fit or best-fit."));
}

static int RunAssign(string csv, string couriersPath, string policyName, string configName, string outputFile)
{
    var result = ResolveAssignmentPolicy(policyName).Bind(choice =>
        ResolveScoreConfig(configName).Bind(config =>
            AssignmentWorkflow.Run(csv, couriersPath, outputFile, EligibilityRules.Default, Scoring.CreateScore(config), new NativeEffortCalculator(), choice.Policy, choice.Name)));

    return Report(result, run =>
    {
        Console.WriteLine($"Assignment policy: {run.Plan.PolicyName}, score: {configName.Trim().ToLowerInvariant()}");
        Console.WriteLine();
        Console.WriteLine("Orders in dispatch order");
        foreach (var outcome in run.Plan.Outcomes)
        {
            var line = outcome switch
            {
                AssignmentOutcome.Assigned a => $"  {a.Order.Request.Id,-6} {a.Order.Points,5} points -> {a.Courier.Id} ({a.Courier.Name}), {a.RemainingCapacity} left",
                AssignmentOutcome.Unassigned u => $"  {u.Order.Request.Id,-6} {u.Order.Points,5} points -> NOT ASSIGNED: {u.Reason}",
                _ => "  unknown outcome",
            };
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Console.WriteLine("Courier loads");
        Console.WriteLine($"  {"Courier",-8} {"Name",-10} {"Capacity",9} {"Used",6} {"Orders",7}");
        foreach (var load in run.Plan.Loads)
        {
            Console.WriteLine($"  {load.CourierId,-8} {load.Name,-10} {load.Capacity,9} {load.Used,6} {load.Orders,7}");
        }

        Console.WriteLine();
        Console.WriteLine($"Exported to {run.OutputPath}");
    });
}

static int RunReports(string csv, string policyName, string configName, string outputFile)
{
    var result = ResolvePolicy(policyName).Bind(choice =>
        ResolveScoreConfig(configName).Bind(config =>
            ReportingWorkflow.Run(csv, outputFile, EligibilityRules.Default, choice.Policy, Scoring.CreateScore(config), new NativeEffortCalculator())));

    return Report(result, run =>
    {
        Console.WriteLine($"Policy: {policyName.Trim().ToLowerInvariant()}, score: {configName.Trim().ToLowerInvariant()}");
        Console.WriteLine();
        Console.WriteLine("Report 1 - Dispatch list (highest score first)");
        Console.WriteLine($"  {"Rank",-5} {"Id",-6} {"Customer",-10} {"Km",6} {"Kg",6} {"Price",8} {"Score",8}");
        foreach (var line in run.Bundle.Dispatch)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {line.Rank,-5} {line.Id,-6} {line.Customer,-10} {line.DistanceKm,6:0.##} {line.WeightKg,6:0.##} {line.Price,8:0} {line.Score,8:0.##}"));
        }

        Console.WriteLine();
        Console.WriteLine("Report 2 - Customer workload (native effort points)");
        Console.WriteLine($"  {"Customer",-10} {"Orders",6} {"Km",7} {"Points",7} {"Heaviest kg",12}");
        foreach (var line in run.Bundle.Workload)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {line.Customer,-10} {line.Orders,6} {line.TotalKm,7:0.##} {line.TotalPoints,7} {line.HeaviestKg,12:0.##}"));
        }

        Console.WriteLine();
        Console.WriteLine($"Quote summary (pipeline): {run.Bundle.Summary.Quotes.Length} quotes, total {run.Bundle.Summary.TotalPrice:0} KZT, not eligible: {run.Bundle.Summary.SkippedCount}");
        Console.WriteLine($"Exported to {run.OutputPath}");
    });
}

internal sealed record PolicyChoice(string Name, Func<DeliveryRequest, decimal> Policy);

internal sealed record AssignmentPolicyChoice(string Name, Func<System.Collections.Immutable.ImmutableArray<CourierLoad>, int, Option<CourierLoad>> Policy);
