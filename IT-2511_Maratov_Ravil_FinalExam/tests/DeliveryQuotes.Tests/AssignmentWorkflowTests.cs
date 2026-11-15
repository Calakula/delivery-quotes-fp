using System.Collections.Immutable;
using System.Text.Json;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member C: workflow 5 (courier assignment): complete runs, failure paths, state preservation.
public class AssignmentWorkflowTests
{
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dq-asg-" + Guid.NewGuid().ToString("N"));
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

    private static Result<AssignmentRun> Run(string orders, string couriers, string output, IEffortCalculator effort, Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>>? policy = null) =>
        AssignmentWorkflow.Run(orders, couriers, output, EligibilityRules.Default, Scoring.CreateScore(ScoreConfig.DistanceFirst), effort, policy ?? AssignmentPolicies.FirstFit, "first-fit");

    private static ImmutableArray<Courier> Couriers(params int[] capacities) =>
        capacities.Select((capacity, i) => Assert.IsType<Result<Courier>.Ok>(Courier.Create($"C-{i + 1}", $"Courier {i + 1}", capacity)).Value).ToImmutableArray();

    // COMPLETE WORKFLOW: normal path (real native library, two JSON files in, one JSON file out)
    [Fact]
    public void CompleteWorkflow_NormalPath_LoadsPlansWithNativePointsAndExports()
    {
        using var dir = new TempDir();
        var orders = dir.CopySample("deliveries.csv");
        var couriers = dir.CopySample("couriers.json");
        var output = dir.File("plan.json");

        var run = Assert.IsType<Result<AssignmentRun>.Ok>(Run(orders, couriers, output, new NativeEffortCalculator())).Value;

        Assert.Equal(7, run.Plan.Outcomes.OfType<AssignmentOutcome.Assigned>().Count());
        Assert.Equal("D-05", Assert.IsType<AssignmentOutcome.Unassigned>(run.Plan.Outcomes[0]).Order.Request.Id);
        Assert.Equal(new[] { 288, 189, 144 }, run.Plan.Loads.Select(l => l.Used));   // points from the real native calls
        using var document = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal("first-fit", document.RootElement.GetProperty("Policy").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("Assigned").GetArrayLength());
        Assert.Equal("D-05", document.RootElement.GetProperty("Unassigned")[0].GetProperty("OrderId").GetString());
        Assert.False(File.Exists(output + ".tmp"));
    }

    [Fact]
    public void Workflow_TwoPolicies_OnTheSameFiles_ProduceDifferentPlans()
    {
        using var dir = new TempDir();
        var orders = dir.CopySample("deliveries.csv");
        var couriers = dir.CopySample("couriers.json");

        var first = Assert.IsType<Result<AssignmentRun>.Ok>(Run(orders, couriers, dir.File("p1.json"), new NativeEffortCalculator(), AssignmentPolicies.FirstFit)).Value;
        var best = Assert.IsType<Result<AssignmentRun>.Ok>(Run(orders, couriers, dir.File("p2.json"), new NativeEffortCalculator(), AssignmentPolicies.BestFit)).Value;

        Assert.Equal(new[] { 2, 3, 2 }, first.Plan.Loads.Select(l => l.Orders));
        Assert.Equal(new[] { 4, 2, 1 }, best.Plan.Loads.Select(l => l.Orders));
    }

    // FAILURE PATHS: each reports the problem and leaves everything consistent
    [Fact]
    public void Plan_NoCouriers_IsADomainFailure_AndNothingIsCalculated()
    {
        var calls = 0;
        var effort = new NativeEffortCalculator((m, g) =>
        {
            calls++;
            return NativeEffortCalculator.RawLoadPoints(m, g);
        });

        var result = AssignmentWorkflow.Plan(Deliveries(), ImmutableArray<Courier>.Empty, EligibilityRules.Default,
            Scoring.CreateScore(ScoreConfig.DistanceFirst), effort, AssignmentPolicies.FirstFit, "first-fit");

        var failure = Assert.IsType<Result<AssignmentPlan>.Failure>(result);
        Assert.Equal(ErrorCode.InvalidInput, failure.Error.Code);
        Assert.Contains("No couriers", failure.Error.Message);
        Assert.Equal(0, calls);   // the dependent steps (native calls, planning) were skipped
    }

    [Fact]
    public void Plan_NativeFailure_StopsAtTheFirstFailingOrder_WithNoPartialPlan()
    {
        var calls = 0;
        var flaky = new NativeEffortCalculator((m, g) =>
        {
            calls++;
            return calls == 2 ? throw new DllNotFoundException("deliveryscore") : NativeEffortCalculator.RawLoadPoints(m, g);
        });

        var result = AssignmentWorkflow.Plan(Deliveries(), Couriers(300, 200), EligibilityRules.Default,
            Scoring.CreateScore(ScoreConfig.DistanceFirst), flaky, AssignmentPolicies.FirstFit, "first-fit");

        Assert.Equal(ErrorCode.NativeLibraryUnavailable, Assert.IsType<Result<AssignmentPlan>.Failure>(result).Error.Code);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Workflow_MissingOrMalformedFiles_AreReported_AndNothingIsExported()
    {
        using var dir = new TempDir();
        var orders = dir.CopySample("deliveries.csv");
        var couriers = dir.CopySample("couriers.json");
        var badCouriers = dir.CopySample("couriers_invalid.json");
        var output = dir.File("plan.json");
        var effort = new NativeEffortCalculator();

        var missingOrders = Run(dir.File("nope.csv"), couriers, output, effort);
        var missingCouriers = Run(orders, dir.File("nope.json"), output, effort);
        var malformedCouriers = Run(orders, badCouriers, output, effort);

        Assert.Equal(ErrorCode.FileNotFound, Assert.IsType<Result<AssignmentRun>.Failure>(missingOrders).Error.Code);
        Assert.Equal(ErrorCode.FileNotFound, Assert.IsType<Result<AssignmentRun>.Failure>(missingCouriers).Error.Code);
        var malformed = Assert.IsType<Result<AssignmentRun>.Failure>(malformedCouriers);
        Assert.Equal(ErrorCode.MalformedContent, malformed.Error.Code);
        Assert.Contains("entry 2", malformed.Error.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Workflow_UnwritableOutput_IsReported_AndTheInputFilesStayUnchanged()
    {
        using var dir = new TempDir();
        var orders = dir.CopySample("deliveries.csv");
        var couriers = dir.CopySample("couriers.json");
        var ordersBefore = File.ReadAllText(orders);
        var couriersBefore = File.ReadAllText(couriers);
        var output = dir.File("plan.json");
        Directory.CreateDirectory(output);   // the output name is a folder

        var result = Run(orders, couriers, output, new NativeEffortCalculator());

        Assert.Equal(ErrorCode.FileNotWritable, Assert.IsType<Result<AssignmentRun>.Failure>(result).Error.Code);
        Assert.Equal(ordersBefore, File.ReadAllText(orders));
        Assert.Equal(couriersBefore, File.ReadAllText(couriers));
    }

    [Fact]
    public void Plan_NoEligibleOrders_IsASuccessWithAnEmptyPlan()
    {
        var onlyOutside = ImmutableArray.Create(Req("D-07", 31m, 2m), Req("D-09", 6m, 25m));

        var plan = Assert.IsType<Result<AssignmentPlan>.Ok>(AssignmentWorkflow.Plan(onlyOutside, Couriers(100), EligibilityRules.Default,
            Scoring.CreateScore(ScoreConfig.DistanceFirst), new NativeEffortCalculator(), AssignmentPolicies.FirstFit, "first-fit")).Value;

        Assert.Empty(plan.Outcomes);
        Assert.Equal(0, plan.Loads[0].Used);
    }
}
