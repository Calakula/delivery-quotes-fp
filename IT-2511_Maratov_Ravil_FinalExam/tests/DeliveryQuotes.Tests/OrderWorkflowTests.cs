using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member B: states, transitions and pattern matching (pure workflow, no book).
public class OrderWorkflowTests
{
    // DISTINCT CASES: every declared case is reachable and matched
    [Fact]
    public void DistinctCases_EveryCaseIsReachableAndHandledByPatternMatching()
    {
        var request = Req("R-001", 2m, 1m);

        // RequestValidation: Valid / Invalid
        Assert.Equal("valid R-001", Label(DeliveryRequest.Create("R-001", "Test", 2m, 1m)));
        Assert.Equal("invalid: Id is empty.", Label(DeliveryRequest.Create("", "Test", 2m, 1m)));

        // OrderState: Submitted / Quoted / Confirmed (each carries different data)
        Assert.Equal("R-001: submitted (2 km, 1 kg), waiting for a quote", OrderWorkflow.Describe(SubmittedState(request)));
        Assert.Equal("R-001: quoted 840 KZT (standard), waiting for confirmation", OrderWorkflow.Describe(QuotedState(request)));
        Assert.Equal("R-001: confirmed #7, 840 KZT (standard)", OrderWorkflow.Describe(ConfirmedState(request, 7)));

        // TransitionResult: Moved / Refused
        Assert.Equal("moved", Label(OrderWorkflow.Quote(SubmittedState(request), EligibilityRules.Default, PricingPolicies.Standard, "standard")));
        Assert.Equal("refused", Label(OrderWorkflow.Confirm(SubmittedState(request), 1)));
    }

    private static string Label(RequestValidation v) => v switch
    {
        RequestValidation.Valid valid => "valid " + valid.Request.Id,
        RequestValidation.Invalid invalid => "invalid: " + string.Join(" ", invalid.Reasons),
        _ => "unknown",
    };

    private static string Label(TransitionResult r) => r switch
    {
        TransitionResult.Moved => "moved",
        TransitionResult.Refused => "refused",
        _ => "unknown",
    };

    // NORMAL OPERATION
    [Fact]
    public void Quote_SubmittedOrder_MovesToQuotedWithThePolicyPrice()
    {
        var submitted = SubmittedState(Req("R-001", 2m, 1m));

        var result = OrderWorkflow.Quote(submitted, EligibilityRules.Default, PricingPolicies.Express, "express");

        var quoted = Assert.IsType<OrderState.Quoted>(Assert.IsType<TransitionResult.Moved>(result).Next);
        Assert.Equal(1360m, quoted.Price);
        Assert.Equal("express", quoted.PolicyName);
        Assert.Equal("R-001", quoted.Request.Id);
    }

    // BOUNDARY VALUE (service limits and confirmation number)
    [Fact]
    public void Boundary_ExactServiceLimitIsQuoted_JustAboveIsRefused()
    {
        var onLimit = SubmittedState(Req("A", 30m, 20m));
        var overDistance = SubmittedState(Req("B", 30.01m, 1m));
        var overWeight = SubmittedState(Req("C", 1m, 20.01m));

        Assert.IsType<TransitionResult.Moved>(OrderWorkflow.Quote(onLimit, EligibilityRules.Default, PricingPolicies.Standard, "standard"));
        var refused = Assert.IsType<TransitionResult.Refused>(OrderWorkflow.Quote(overDistance, EligibilityRules.Default, PricingPolicies.Standard, "standard"));
        Assert.Contains("outside the service limits", refused.Message);
        Assert.IsType<TransitionResult.Refused>(OrderWorkflow.Quote(overWeight, EligibilityRules.Default, PricingPolicies.Standard, "standard"));
    }

    [Fact]
    public void Boundary_ConfirmationNumberMustBePositive()
    {
        var quoted = QuotedState(Req("R-001", 2m, 1m));

        Assert.IsType<TransitionResult.Refused>(OrderWorkflow.Confirm(quoted, 0));
        Assert.IsType<TransitionResult.Refused>(OrderWorkflow.Confirm(quoted, -1));
        var moved = Assert.IsType<TransitionResult.Moved>(OrderWorkflow.Confirm(quoted, 1));
        Assert.Equal(1, Assert.IsType<OrderState.Confirmed>(moved.Next).ConfirmationNumber);
    }

    // MISSING PREREQUISITE
    [Fact]
    public void Confirm_BeforeQuote_IsRefused_NotAnInvalidSuccess()
    {
        var submitted = SubmittedState(Req("R-001", 2m, 1m));

        var result = OrderWorkflow.Confirm(submitted, 1);

        var refused = Assert.IsType<TransitionResult.Refused>(result);
        Assert.Contains("no quote yet", refused.Message);
    }

    // REPEATED OPERATION
    [Fact]
    public void Confirm_Repeated_ReturnsTheExistingConfirmationUnchanged()
    {
        var first = Assert.IsType<TransitionResult.Moved>(OrderWorkflow.Confirm(QuotedState(Req("R-001", 2m, 1m)), 1));

        var second = Assert.IsType<TransitionResult.Moved>(OrderWorkflow.Confirm(first.Next, 99));

        Assert.Same(first.Next, second.Next);
        Assert.Equal(1, Assert.IsType<OrderState.Confirmed>(second.Next).ConfirmationNumber);
    }

    [Fact]
    public void Quote_Repeated_IsRefused_AndKeepsTheOriginalPrice()
    {
        var quoted = QuotedState(Req("R-001", 2m, 1m)); // Standard: 840

        var again = OrderWorkflow.Quote(quoted, EligibilityRules.Default, PricingPolicies.Express, "express");

        Assert.IsType<TransitionResult.Refused>(again);
        Assert.Equal(840m, Assert.IsType<OrderState.Quoted>(quoted).Price); // the original state object is untouched
    }

    [Fact]
    public void Quote_PolicyReturningNegativePrice_IsRefused()
    {
        var submitted = SubmittedState(Req("R-001", 2m, 1m));

        var result = OrderWorkflow.Quote(submitted, EligibilityRules.Default, _ => -5m, "broken");

        Assert.Contains("negative price", Assert.IsType<TransitionResult.Refused>(result).Message);
    }
}
