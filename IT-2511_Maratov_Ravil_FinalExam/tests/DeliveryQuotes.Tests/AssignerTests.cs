using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member B: the pure planning step and the two courier-choice policies.
public class AssignerTests
{
    private static Courier NewCourier(string id, string name, int capacity) =>
        Assert.IsType<Result<Courier>.Ok>(Courier.Create(id, name, capacity)).Value;

    private static ImmutableArray<Courier> ThreeCouriers() =>
        ImmutableArray.Create(NewCourier("C-1", "Aibek", 300), NewCourier("C-2", "Bota", 200), NewCourier("C-3", "Sanzhar", 150));

    // The eligible orders of data/deliveries.csv in dispatch order (distance-first score) with hand-calculated points.
    private static ImmutableArray<EffortItem> DispatchOrder() =>
        new[] { "D-05", "D-06", "D-10", "D-02", "D-04", "D-03", "D-01", "D-08" }
            .Select(id => new EffortItem(Deliveries().Single(r => r.Id == id), HandPoints[id]))
            .ToImmutableArray();

    private static string[] AssignedTo(AssignmentPlan plan) =>
        plan.Outcomes.OfType<AssignmentOutcome.Assigned>().Select(a => $"{a.Order.Request.Id}->{a.Courier.Id}").ToArray();

    [Fact]
    public void FirstFit_AssignsEachOrderToTheFirstCourierWithEnoughCapacity()
    {
        var plan = Assigner.Plan(DispatchOrder(), ThreeCouriers(), AssignmentPolicies.FirstFit, "first-fit");

        Assert.Equal(new[] { "D-06->C-1", "D-10->C-1", "D-02->C-2", "D-04->C-3", "D-03->C-2", "D-01->C-2", "D-08->C-3" }, AssignedTo(plan));
        Assert.Equal(new[] { 288, 189, 144 }, plan.Loads.Select(l => l.Used));
        Assert.Equal(new[] { 2, 3, 2 }, plan.Loads.Select(l => l.Orders));
        Assert.Equal(new[] { 156, 12, 80, 30, 35, 11, 6 }, plan.Outcomes.OfType<AssignmentOutcome.Assigned>().Select(a => a.RemainingCapacity));
    }

    [Fact]
    public void BestFit_AssignsToTheCourierWithTheLeastCapacityLeft()
    {
        var plan = Assigner.Plan(DispatchOrder(), ThreeCouriers(), AssignmentPolicies.BestFit, "best-fit");

        Assert.Equal(new[] { "D-06->C-3", "D-10->C-2", "D-02->C-1", "D-04->C-1", "D-03->C-2", "D-01->C-1", "D-08->C-1" }, AssignedTo(plan));
        Assert.Equal(new[] { 288, 189, 144 }, plan.Loads.Select(l => l.Used));
        Assert.Equal(new[] { 4, 2, 1 }, plan.Loads.Select(l => l.Orders));
    }

    [Fact]
    public void TwoPolicies_SameInput_GiveTheDocumentedDifference()
    {
        var first = Assigner.Plan(DispatchOrder(), ThreeCouriers(), AssignmentPolicies.FirstFit, "first-fit");
        var best = Assigner.Plan(DispatchOrder(), ThreeCouriers(), AssignmentPolicies.BestFit, "best-fit");

        Assert.Contains("D-06->C-1", AssignedTo(first));   // first-fit takes the first courier ...
        Assert.Contains("D-06->C-3", AssignedTo(best));    // ... best-fit takes the tightest fit
        Assert.NotEqual(AssignedTo(first), AssignedTo(best));
        Assert.Equal(first.Outcomes.Length, best.Outcomes.Length);   // the same orders are handled by both
    }

    [Fact]
    public void OrderLargerThanEveryCourier_IsUnassigned_WithItsOwnReason()
    {
        var plan = Assigner.Plan(DispatchOrder(), ThreeCouriers(), AssignmentPolicies.FirstFit, "first-fit");

        var unassigned = Assert.IsType<AssignmentOutcome.Unassigned>(plan.Outcomes[0]);
        Assert.Equal("D-05", unassigned.Order.Request.Id);
        Assert.Contains("332 points, more than the largest courier capacity (300)", unassigned.Reason);
        Assert.Equal(1, plan.Outcomes.OfType<AssignmentOutcome.Unassigned>().Count());
    }

    [Fact]
    public void WhenCapacityIsUsedUp_TheNextOrderIsUnassigned_WithADifferentReason()
    {
        var couriers = ImmutableArray.Create(NewCourier("C-1", "Aibek", 100));
        var orders = ImmutableArray.Create(
            new EffortItem(Req("A", 1m, 1m), 60),
            new EffortItem(Req("B", 1m, 1m), 60),     // only 40 left
            new EffortItem(Req("C", 1m, 1m), 40));    // exactly the capacity that is left

        var plan = Assigner.Plan(orders, couriers, AssignmentPolicies.FirstFit, "first-fit");

        Assert.Equal(new[] { "A->C-1", "C->C-1" }, AssignedTo(plan));
        Assert.Contains("no courier has 60 points of capacity left", Assert.IsType<AssignmentOutcome.Unassigned>(plan.Outcomes[1]).Reason);
        Assert.Equal(0, plan.Outcomes.OfType<AssignmentOutcome.Assigned>().Last().RemainingCapacity);   // exact fit uses the capacity fully
    }

    [Fact]
    public void BestFit_TiesAreBrokenByCourierId_AndInputsAreNotChanged()
    {
        var couriers = ImmutableArray.Create(NewCourier("C-2", "Bota", 100), NewCourier("C-1", "Aibek", 100));
        var snapshot = couriers.ToArray();
        var orders = ImmutableArray.Create(new EffortItem(Req("A", 1m, 1m), 30));

        var plan = Assigner.Plan(orders, couriers, AssignmentPolicies.BestFit, "best-fit");

        Assert.Equal(new[] { "A->C-1" }, AssignedTo(plan));   // equal remaining capacity: C-1 before C-2
        Assert.Equal(snapshot, couriers.ToArray());
        Assert.Equal(new[] { "C-2", "C-1" }, plan.Loads.Select(l => l.CourierId));
    }

    [Fact]
    public void EmptyOrders_GiveAnEmptyPlan_WithTheCouriersAtZeroLoad()
    {
        var plan = Assigner.Plan(Array.Empty<EffortItem>(), ThreeCouriers(), AssignmentPolicies.FirstFit, "first-fit");

        Assert.Empty(plan.Outcomes);
        Assert.Equal(new[] { 0, 0, 0 }, plan.Loads.Select(l => l.Used));
        Assert.Empty(plan.ToExport().Assigned);
    }

    [Fact]
    public void ZeroCapacityCourier_TakesNothing_AndAPolicyThatBreaksTheRuleIsRejectedSafely()
    {
        var couriers = ImmutableArray.Create(NewCourier("C-1", "Day off", 0));
        var orders = ImmutableArray.Create(new EffortItem(Req("A", 1m, 1m), 5));

        var normal = Assigner.Plan(orders, couriers, AssignmentPolicies.FirstFit, "first-fit");
        var broken = Assigner.Plan(orders, couriers, (loads, points) => Option.Some(loads[0]), "broken");   // ignores the capacity

        Assert.IsType<AssignmentOutcome.Unassigned>(normal.Outcomes[0]);
        Assert.Contains("without enough capacity", Assert.IsType<AssignmentOutcome.Unassigned>(broken.Outcomes[0]).Reason);
        Assert.Equal(0, broken.Loads[0].Used);   // no capacity was exceeded
    }

    [Fact]
    public void Policy_IsInvokedOncePerOrder_AndPolicyLookupWorks()
    {
        var calls = 0;
        Option<CourierLoad> Counting(ImmutableArray<CourierLoad> loads, int points)
        {
            calls++;
            return AssignmentPolicies.FirstFit(loads, points);
        }

        Assigner.Plan(DispatchOrder(), ThreeCouriers(), Counting, "counting");

        Assert.Equal(8, calls);
        Assert.IsType<Option<Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>>>.Some>(AssignmentPolicies.Find(" Best-Fit "));
        Assert.IsType<Option<Func<ImmutableArray<CourierLoad>, int, Option<CourierLoad>>>.None>(AssignmentPolicies.Find("random"));
    }

    [Fact]
    public void ExportData_FlattensTheOutcomesByPatternMatching()
    {
        var export = Assigner.Plan(DispatchOrder(), ThreeCouriers(), AssignmentPolicies.FirstFit, "first-fit").ToExport();

        Assert.Equal(7, export.Assigned.Length);
        Assert.Equal("D-05", Assert.Single(export.Unassigned).OrderId);
        Assert.Equal("C-1", export.Assigned[0].CourierId);
        Assert.Equal(3, export.Loads.Length);
    }
}
