using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member A: validated factory and prevention of unchecked construction.
public class FactoryTests
{
    // FACTORY SUCCESS
    [Fact]
    public void Create_ValidInput_ReturnsValidWithNormalisedData()
    {
        var result = DeliveryRequest.Create("  r-003 ", " Nurlan ", 30m, 8m);

        var valid = Assert.IsType<RequestValidation.Valid>(result);
        Assert.Equal("R-003", valid.Request.Id);
        Assert.Equal("Nurlan", valid.Request.Customer);
        Assert.Equal(30m, valid.Request.DistanceKm);
        Assert.Equal(8m, valid.Request.WeightKg);
    }

    // FACTORY REJECTION: every rule rejects and names its reason
    [Fact]
    public void Create_EachRuleRejects_AndReportsItsReason()
    {
        var cases = new (string? Id, string? Customer, decimal Km, decimal Kg, string Reason)[]
        {
            (null, "Dana", 4m, 2m, "Id is empty"),
            ("   ", "Dana", 4m, 2m, "Id is empty"),
            ("R-1", "", 4m, 2m, "Customer is empty"),
            ("R-1", null, 4m, 2m, "Customer is empty"),
            ("R-1", "Dana", 0m, 2m, "DistanceKm must be greater than 0"),
            ("R-1", "Dana", -3m, 2m, "DistanceKm must be greater than 0"),
            ("R-1", "Dana", 4m, -0.5m, "WeightKg must not be negative"),
        };

        foreach (var c in cases)
        {
            var invalid = Assert.IsType<RequestValidation.Invalid>(DeliveryRequest.Create(c.Id, c.Customer, c.Km, c.Kg));
            Assert.Single(invalid.Reasons);
            Assert.Contains(c.Reason, invalid.Reasons[0]);
        }
    }

    [Fact]
    public void Create_SeveralProblems_AreAllReportedTogether()
    {
        var invalid = Assert.IsType<RequestValidation.Invalid>(DeliveryRequest.Create(" ", "", -1m, -1m));

        Assert.Equal(4, invalid.Reasons.Length);
    }

    // BOUNDARY VALUE
    [Fact]
    public void Create_ExactBoundaries_FollowTheRules()
    {
        Assert.IsType<RequestValidation.Valid>(DeliveryRequest.Create("R-1", "Dana", 0.01m, 0m));      // smallest distance, zero weight
        Assert.IsType<RequestValidation.Invalid>(DeliveryRequest.Create("R-1", "Dana", 0m, 0m));       // distance 0 is not allowed
        Assert.IsType<RequestValidation.Invalid>(DeliveryRequest.Create("R-1", "Dana", 1m, -0.01m));   // weight just below 0
    }

    // UNCHECKED CONSTRUCTION IS NOT POSSIBLE (access restrictions)
    [Fact]
    public void NoUncheckedConstruction_NoPublicConstructorsAndNoSetters()
    {
        Assert.Empty(typeof(DeliveryRequest).GetConstructors());
        Assert.All(typeof(DeliveryRequest).GetProperties(), p => Assert.Null(p.SetMethod));

        // order states can only be reached through the workflow, never constructed by callers
        Assert.Empty(typeof(OrderState.Submitted).GetConstructors());
        Assert.Empty(typeof(OrderState.Quoted).GetConstructors());
        Assert.Empty(typeof(OrderState.Confirmed).GetConstructors());
        Assert.All(typeof(OrderState.Confirmed).GetProperties().Where(p => p.DeclaringType == typeof(OrderState.Confirmed)), p => Assert.Null(p.SetMethod));
    }
}
