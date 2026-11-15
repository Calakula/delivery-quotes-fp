using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member C: result chain B on the order book and the required lookup.
public class OrderServiceTests
{
    private static OrderBook SampleBook() =>
        Assert.IsType<Result<OrderBook>.Ok>(OrderService.LoadBook(ImmutableArray.Create(
            Req("R-001", 2m, 1m, "Aigerim"),
            Req("R-004", 31m, 2m, "Aruzhan")))).Value;

    private static Result<ProcessOutcome> Process(OrderBook book, string? id, ProcessAction action, DiscountTable? discounts = null, Func<DeliveryRequest, decimal>? policy = null) =>
        OrderService.Process(book, id, action, EligibilityRules.Default, policy ?? PricingPolicies.Standard, "standard", discounts ?? DiscountTable.Empty, 1);

    // COMPLETE SUCCESS
    [Fact]
    public void FullChain_FindsQuotesConfirmsAndMapsToTheDisplayModel()
    {
        var book = SampleBook();

        var outcome = Assert.IsType<Result<ProcessOutcome>.Ok>(Process(book, "r-001", ProcessAction.Full)).Value;

        Assert.Equal("Confirmed", outcome.View.Status);
        Assert.Equal("R-001: confirmed #1, 840 KZT (standard)", outcome.View.Text);
        Assert.True(outcome.Book.TryGet("R-001", out var stored));
        Assert.Equal(840m, Assert.IsType<OrderState.Confirmed>(stored).Price);
        // the original book was not touched
        Assert.True(book.TryGet("R-001", out var original));
        Assert.IsType<OrderState.Submitted>(original);
    }

    [Fact]
    public void Discount_IsAppliedInsideTheChain_AndShownInThePolicyName()
    {
        var table = Assert.IsType<Result<DiscountTable>.Ok>(
            DiscountTable.Create(new[] { new KeyValuePair<string, decimal>("Aigerim", 10m) })).Value;

        var outcome = Assert.IsType<Result<ProcessOutcome>.Ok>(Process(SampleBook(), "R-001", ProcessAction.Full, table)).Value;

        Assert.Equal("R-001: confirmed #1, 756 KZT (standard, -10%)", outcome.View.Text);
    }

    // LOOKUP PRESENT AND ABSENT (required item)
    [Fact]
    public void RequiredLookup_PresentIsFound_AbsentBecomesNotFound()
    {
        var book = SampleBook();

        Assert.IsType<OrderState.Submitted>(Assert.IsType<Option<OrderState>.Some>(book.Find(" r-001 ")).Value);
        Assert.IsType<Option<OrderState>.None>(book.Find("R-404"));
        Assert.IsType<Option<OrderState>.None>(book.Find(null));
        Assert.IsType<Option<OrderState>.None>(book.Find(""));

        var failure = Assert.IsType<Result<ProcessOutcome>.Failure>(Process(book, "R-404", ProcessAction.Full));
        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
        Assert.Contains("R-404", failure.Error.Message);
    }

    [Fact]
    public void PolicyLookup_PresentAndAbsent()
    {
        Assert.IsType<Option<Func<DeliveryRequest, decimal>>.Some>(PricingPolicies.Find(" Standard "));
        Assert.IsType<Option<Func<DeliveryRequest, decimal>>.None>(PricingPolicies.Find("cheap"));
        Assert.IsType<Option<Func<DeliveryRequest, decimal>>.None>(PricingPolicies.Find(null));
    }

    // DOMAIN FAILURES
    [Fact]
    public void DomainFailures_NotFound_InvalidState_OutsideLimits_EachWithItsCode()
    {
        var book = SampleBook();

        var missing = Assert.IsType<Result<ProcessOutcome>.Failure>(Process(book, "R-404", ProcessAction.Quote)).Error;
        var noQuote = Assert.IsType<Result<ProcessOutcome>.Failure>(Process(book, "R-001", ProcessAction.Confirm)).Error;
        var outside = Assert.IsType<Result<ProcessOutcome>.Failure>(Process(book, "R-004", ProcessAction.Full)).Error;

        Assert.Equal(ErrorCode.NotFound, missing.Code);
        Assert.Equal(ErrorCode.InvalidState, noQuote.Code);
        Assert.Contains("no quote yet", noQuote.Message);
        Assert.Equal(ErrorCode.OutsideServiceLimits, outside.Code);
    }

    // FAILURE PROPAGATION in the real chain
    [Fact]
    public void Failure_SkipsDependentSteps_NoPolicyCall_NoConfirmation_BookUnchanged()
    {
        var policyCalls = 0;
        decimal CountingPolicy(DeliveryRequest r)
        {
            policyCalls++;
            return 100m;
        }
        var book = SampleBook();

        var missing = Process(book, "R-404", ProcessAction.Full, null, CountingPolicy);
        var outside = Process(book, "R-004", ProcessAction.Full, null, CountingPolicy);

        Assert.Equal(0, policyCalls);                       // neither the quote nor anything after it ran
        Assert.Equal(ErrorCode.NotFound, Assert.IsType<Result<ProcessOutcome>.Failure>(missing).Error.Code);
        // the quote of R-004 was refused, so the confirmation step never ran (it would have said InvalidState)
        Assert.Equal(ErrorCode.OutsideServiceLimits, Assert.IsType<Result<ProcessOutcome>.Failure>(outside).Error.Code);
        Assert.True(book.TryGet("R-004", out var state));
        Assert.IsType<OrderState.Submitted>(state);         // state preserved
    }

    // REPEATED OPERATION (documented in README)
    [Fact]
    public void Repeated_ConfirmOnConfirmedSucceedsUnchanged_FullOnConfirmedIsRefused()
    {
        var confirmed = Assert.IsType<Result<ProcessOutcome>.Ok>(Process(SampleBook(), "R-001", ProcessAction.Full)).Value.Book;

        var again = Assert.IsType<Result<ProcessOutcome>.Ok>(Process(confirmed, "R-001", ProcessAction.Confirm)).Value;
        var refused = Assert.IsType<Result<ProcessOutcome>.Failure>(Process(confirmed, "R-001", ProcessAction.Full)).Error;

        Assert.Equal("R-001: confirmed #1, 840 KZT (standard)", again.View.Text);
        Assert.Equal(ErrorCode.InvalidState, refused.Code);
    }

    [Fact]
    public void LoadBook_DuplicateIds_FailWholeLoad_NoPartialBook()
    {
        var result = OrderService.LoadBook(ImmutableArray.Create(Req("R-1", 2m, 1m), Req("r-1", 3m, 1m)));

        Assert.Equal(ErrorCode.DuplicateOrder, Assert.IsType<Result<OrderBook>.Failure>(result).Error.Code);
        Assert.Equal(0, Assert.IsType<Result<OrderBook>.Ok>(OrderService.LoadBook(ImmutableArray<DeliveryRequest>.Empty)).Value.Count); // empty dataset is fine
        Assert.Equal(ErrorCode.InvalidInput, Assert.IsType<Result<OrderBook>.Failure>(OrderService.LoadBook(default)).Error.Code);
    }

    // The Assignment 5 pipeline and the Assignment 6 transitions stay active
    [Fact]
    public void EarlierWorkflows_StillWork_Pipeline8130_AndLifecycleConfirmations()
    {
        var requests = ImmutableArray.Create(
            Req("R-001", 2m, 1m), Req("R-002", 10m, 5m), Req("R-003", 30m, 8m), Req("R-004", 31m, 2m), Req("R-005", 4.5m, 0m), Req("R-006", 6m, 25m));

        var summary = DeliveryPipeline.Run(requests, EligibilityRules.Default, PricingPolicies.Standard);
        var lifecycle = OrderProcessor.Run(requests, EligibilityRules.Default, PricingPolicies.Standard, "standard");

        Assert.Equal(8130m, summary.TotalPrice);
        Assert.Equal(19, lifecycle.Steps.Length);
        Assert.Equal(4, lifecycle.Steps.Count(s => s.Action == "Confirm" && s.Result is TransitionResult.Moved));
    }
}
