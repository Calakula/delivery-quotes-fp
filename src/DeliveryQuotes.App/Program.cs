using DeliveryQuotes.Core;

// Impure shell: reads the file, chooses the policy, prints. All calculations are in DeliveryQuotes.Core.
// Usage: dotnet run --project src/DeliveryQuotes.App -- <csv-path> <standard|express|both>
// Exit codes: 0 ok, 1 file cannot be read, 2 invalid data, 64 bad arguments.

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: <csv-path> <standard|express|both>");
    return 64;
}

var path = args[0];
var mode = args[1].Trim().ToLowerInvariant();

var policyNames = mode == "both" ? new[] { "standard", "express" } : new[] { mode };
foreach (var name in policyNames)
{
    if (!PricingPolicies.TryGetByName(name, out _))
    {
        Console.Error.WriteLine($"Unknown policy '{args[1]}'. Use standard, express or both.");
        return 64;
    }
}

string[] lines;
try
{
    lines = File.ReadAllLines(path);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Cannot read '{path}': {ex.Message}");
    return 1;
}

var report = RequestParser.ParseLines(lines);
if (!report.IsValid)
{
    Console.Error.WriteLine($"Input rejected, {report.Errors.Length} problem(s):");
    foreach (var error in report.Errors)
    {
        Console.Error.WriteLine($"  {error}");
    }
    return 2; // no quotes are produced from invalid input
}

var totals = new List<decimal>();
foreach (var name in policyNames)
{
    PricingPolicies.TryGetByName(name, out var policy);
    var summary = DeliveryPipeline.Run(report.Requests, EligibilityRules.Default, policy!);
    Print(name, summary);
    totals.Add(summary.TotalPrice);
}

if (totals.Count == 2)
{
    Console.WriteLine($"Express minus standard total: {totals[1] - totals[0]:0} KZT");
}
return 0;

static void Print(string policyName, QuoteSummary summary)
{
    Console.WriteLine($"Policy: {policyName}");
    Console.WriteLine($"Quotes: {summary.Quotes.Length} (not eligible: {summary.SkippedCount})");
    foreach (var q in summary.Quotes)
    {
        Console.WriteLine($"  {q.Id,-6} {q.Customer,-10} {q.Price,7:0} KZT");
    }
    Console.WriteLine($"Total: {summary.TotalPrice:0} KZT");
    Console.WriteLine();
}
