using System.Collections.Immutable;
using System.Text.Json;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member C: complete workflows (file -> reports -> native -> export) and file boundaries. Temporary folders are cleaned up.
public class ReportingWorkflowTests
{
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dq-wf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string CopySample(string name)
        {
            var target = File(name);
            System.IO.File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "data", name), target);
            return target;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static Result<ReportRun> Run(string orders, string output, IEffortCalculator effort) =>
        ReportingWorkflow.Run(orders, output, EligibilityRules.Default, PricingPolicies.Standard, Scoring.CreateScore(ScoreConfig.DistanceFirst), effort);

    // COMPLETE WORKFLOW: normal path with the real native library
    [Fact]
    public void CompleteWorkflow_NormalPath_LoadsReportsCallsNativeAndExports()
    {
        using var dir = new TempDir();
        var orders = dir.CopySample("deliveries.csv");
        var output = dir.File("reports.json");

        var run = Assert.IsType<Result<ReportRun>.Ok>(Run(orders, output, new NativeEffortCalculator())).Value;

        Assert.Equal(new[] { "D-05", "D-06", "D-10", "D-02", "D-04", "D-03", "D-01", "D-08" }, run.Bundle.Dispatch.Select(l => l.Id));
        Assert.Equal(new[] { "Dana", "Aigerim", "Madina", "Nurlan", "Timur" }, run.Bundle.Workload.Select(l => l.Customer));
        Assert.Equal(new[] { 452, 189, 144, 144, 24 }, run.Bundle.Workload.Select(l => l.TotalPoints));   // from the real native calls
        Assert.Equal(14950m, run.Bundle.Summary.TotalPrice);   // the Assignment 5 pipeline still runs
        Assert.Equal(2, run.Bundle.Summary.SkippedCount);
        using var document = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal(452, document.RootElement.GetProperty("Workload")[0].GetProperty("TotalPoints").GetInt32());
        Assert.Equal(Path.GetFullPath(output), run.OutputPath);
    }

    // COMPLETE WORKFLOW: native failure path keeps everything consistent
    [Fact]
    public void CompleteWorkflow_NativeLibraryFailure_ReportsTheProblem_ExportsNothing_ChangesNothing()
    {
        using var dir = new TempDir();
        var orders = dir.CopySample("deliveries.csv");
        var before = File.ReadAllText(orders);
        var output = dir.File("reports.json");
        var unavailable = new NativeEffortCalculator((m, g) => throw new DllNotFoundException("deliveryscore"));

        var result = Run(orders, output, unavailable);

        var failure = Assert.IsType<Result<ReportRun>.Failure>(result);
        Assert.Equal(ErrorCode.NativeLibraryUnavailable, failure.Error.Code);
        Assert.False(File.Exists(output));                 // nothing was exported
        Assert.False(File.Exists(output + ".tmp"));
        Assert.Equal(before, File.ReadAllText(orders));    // the input is unchanged
    }

    [Fact]
    public void Build_NativeFailureInTheMiddle_GivesNoPartialReport()
    {
        var calls = 0;
        var flaky = new NativeEffortCalculator((m, g) =>
        {
            calls++;
            return calls == 3 ? throw new DllNotFoundException("deliveryscore") : NativeEffortCalculator.RawLoadPoints(m, g);
        });

        var result = ReportingWorkflow.Build(Deliveries(), EligibilityRules.Default, PricingPolicies.Standard,
            Scoring.CreateScore(ScoreConfig.DistanceFirst), flaky);

        Assert.Equal(ErrorCode.NativeLibraryUnavailable, Assert.IsType<Result<ReportBundle>.Failure>(result).Error.Code);
        Assert.Equal(3, calls);   // the loop stopped at the failure: the remaining requests were not calculated
    }

    [Fact]
    public void Build_EmptyAndNoMatchingData_GiveEmptyReports_NotFailures()
    {
        var effort = new NativeEffortCalculator();
        var score = Scoring.CreateScore(ScoreConfig.DistanceFirst);

        var empty = Assert.IsType<Result<ReportBundle>.Ok>(ReportingWorkflow.Build(
            ImmutableArray<DeliveryRequest>.Empty, EligibilityRules.Default, PricingPolicies.Standard, score, effort)).Value;
        var noMatch = Assert.IsType<Result<ReportBundle>.Ok>(ReportingWorkflow.Build(
            ImmutableArray.Create(Req("D-07", 31m, 2m)), EligibilityRules.Default, PricingPolicies.Standard, score, effort)).Value;

        Assert.Empty(empty.Dispatch);
        Assert.Empty(empty.Workload);
        Assert.Empty(noMatch.Dispatch);
        Assert.Empty(noMatch.Workload);
        Assert.Equal(1, noMatch.Summary.SkippedCount);
        Assert.Equal(ErrorCode.InvalidInput, Assert.IsType<Result<ReportBundle>.Failure>(ReportingWorkflow.Build(
            default, EligibilityRules.Default, PricingPolicies.Standard, score, effort)).Error.Code);
    }

    // FILE BOUNDARIES: missing file and malformed content stay safe failures
    [Fact]
    public void Workflow_MissingOrdersFile_IsReported_AndNothingIsExported()
    {
        using var dir = new TempDir();
        var output = dir.File("reports.json");

        var result = Run(dir.File("no-such-orders.csv"), output, new NativeEffortCalculator());

        var failure = Assert.IsType<Result<ReportRun>.Failure>(result);
        Assert.Equal(ErrorCode.FileNotFound, failure.Error.Code);
        Assert.Contains("no-such-orders.csv", failure.Error.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Workflow_MalformedOrdersFile_IsReported_AndNothingIsExported()
    {
        using var dir = new TempDir();
        var orders = dir.File("bad.csv");
        File.WriteAllText(orders, "Id,Customer,DistanceKm,WeightKg\nD-01,Aigerim,2,1\nD-02,Dana,abc,5\n");
        var output = dir.File("reports.json");

        var result = Run(orders, output, new NativeEffortCalculator());

        var failure = Assert.IsType<Result<ReportRun>.Failure>(result);
        Assert.Equal(ErrorCode.MalformedContent, failure.Error.Code);
        Assert.Contains("Line 3", failure.Error.Message);
        Assert.False(File.Exists(output));   // the valid first line did not produce a partial report
    }

    [Fact]
    public void Workflow_UnwritableOutput_IsReported_AndTheInputStaysAsItWas()
    {
        using var dir = new TempDir();
        var orders = dir.CopySample("deliveries.csv");
        var before = File.ReadAllText(orders);
        var output = dir.File("reports.json");
        Directory.CreateDirectory(output);   // the output name is a folder

        var result = Run(orders, output, new NativeEffortCalculator());

        Assert.Equal(ErrorCode.FileNotWritable, Assert.IsType<Result<ReportRun>.Failure>(result).Error.Code);
        Assert.Equal(before, File.ReadAllText(orders));
    }

    [Fact]
    public void EarlierWorkflows_StayActive_PipelineTotalsAndSafeTransitions()
    {
        var earlier = ImmutableArray.Create(
            Req("R-001", 2m, 1m), Req("R-002", 10m, 5m), Req("R-003", 30m, 8m), Req("R-004", 31m, 2m), Req("R-005", 4.5m, 0m), Req("R-006", 6m, 25m));

        var bundle = Assert.IsType<Result<ReportBundle>.Ok>(ReportingWorkflow.Build(
            earlier, EligibilityRules.Default, PricingPolicies.Standard, Scoring.CreateScore(ScoreConfig.HeavyFirst), new NativeEffortCalculator())).Value;
        var lifecycle = OrderProcessor.Run(earlier, EligibilityRules.Default, PricingPolicies.Standard, "standard");

        Assert.Equal(8130m, bundle.Summary.TotalPrice);   // Assignment 5 total
        Assert.Equal(4, lifecycle.Steps.Count(s => s.Action == "Confirm" && s.Result is TransitionResult.Moved));   // Assignment 6 transitions
    }
}
