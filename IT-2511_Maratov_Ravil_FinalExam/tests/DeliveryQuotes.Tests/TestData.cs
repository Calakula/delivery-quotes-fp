using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Shared helpers. Validated values and states can only be created through the real factory and workflow.
internal static class TestData
{
    public static DeliveryRequest Req(string id, decimal km, decimal kg, string customer = "Test") =>
        DeliveryRequest.Create(id, customer, km, kg) switch
        {
            RequestValidation.Valid valid => valid.Request,
            RequestValidation.Invalid invalid => throw new InvalidOperationException("Test data is invalid: " + string.Join(" ", invalid.Reasons)),
            _ => throw new InvalidOperationException("Unknown validation outcome."),
        };

    // The dataset of data/deliveries.csv (10 requests, two of them outside the service limits).
    public static ImmutableArray<DeliveryRequest> Deliveries() => ImmutableArray.Create(
        Req("D-01", 2m, 1m, "Aigerim"), Req("D-02", 10m, 5m, "Aigerim"), Req("D-03", 4.5m, 0m, "Aigerim"),
        Req("D-04", 10m, 5m, "Dana"), Req("D-05", 30m, 8m, "Dana"),
        Req("D-06", 12m, 6m, "Nurlan"), Req("D-07", 31m, 2m, "Nurlan"),
        Req("D-08", 2m, 1m, "Timur"), Req("D-09", 6m, 25m, "Timur"),
        Req("D-10", 12m, 6m, "Madina"));

    // Effort points calculated by hand: one point per full 100 m plus one per full 250 g.
    public static readonly IReadOnlyDictionary<string, int> HandPoints = new Dictionary<string, int>
    {
        ["D-01"] = 24, ["D-02"] = 120, ["D-03"] = 45, ["D-04"] = 120,
        ["D-05"] = 332, ["D-06"] = 144, ["D-08"] = 24, ["D-10"] = 144,
    };

    public static OrderState SubmittedState(DeliveryRequest request) =>
        Assert.IsType<TransitionResult.Moved>(OrderBook.Empty.Submit(request).Result).Next;

    public static OrderState QuotedState(DeliveryRequest request) =>
        Assert.IsType<TransitionResult.Moved>(
            OrderWorkflow.Quote(SubmittedState(request), EligibilityRules.Default, PricingPolicies.Standard, "standard")).Next;

    public static OrderState ConfirmedState(DeliveryRequest request, int number) =>
        Assert.IsType<TransitionResult.Moved>(OrderWorkflow.Confirm(QuotedState(request), number)).Next;
}
