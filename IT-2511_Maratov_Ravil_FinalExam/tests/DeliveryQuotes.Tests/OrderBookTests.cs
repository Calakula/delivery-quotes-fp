using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member C: the order book (stored data stays consistent) and its transitions.
public class OrderBookTests
{
    private static readonly EligibilityRules Rules = EligibilityRules.Default;

    // NORMAL OPERATION: the validated value is used by the real workflow
    [Fact]
    public void NormalOperation_SubmitQuoteConfirm_ReachesConfirmed()
    {
        var request = Req("R-001", 2m, 1m, "Aigerim");

        var (book1, r1) = OrderBook.Empty.Submit(request);
        var (book2, r2) = book1.Quote("r-001", Rules, PricingPolicies.Standard, "standard"); // lookup is case-insensitive
        var (book3, r3) = book2.Confirm("R-001", 1);

        Assert.IsType<TransitionResult.Moved>(r1);
        Assert.IsType<TransitionResult.Moved>(r2);
        Assert.IsType<TransitionResult.Moved>(r3);
        Assert.True(book3.TryGet("R-001", out var state));
        var confirmed = Assert.IsType<OrderState.Confirmed>(state);
        Assert.Equal(840m, confirmed.Price);
        Assert.Equal(1, confirmed.ConfirmationNumber);
        Assert.Equal(1, book3.Count);
    }

    [Fact]
    public void Submit_FactoryOutcome_ValidEntersTheBook_InvalidIsRefused()
    {
        var (withOrder, ok) = OrderBook.Empty.Submit(DeliveryRequest.Create("R-1", "Dana", 4m, 2m));
        var (unchanged, refusal) = OrderBook.Empty.Submit(DeliveryRequest.Create("", "Dana", -4m, 2m));

        Assert.IsType<TransitionResult.Moved>(ok);
        Assert.Equal(1, withOrder.Count);
        var refused = Assert.IsType<TransitionResult.Refused>(refusal);
        Assert.Equal(2, refused.Reasons.Length);   // empty id and negative distance
        Assert.Equal(0, unchanged.Count);
        Assert.Same(OrderBook.Empty, unchanged);
    }

    // MISSING PREREQUISITE
    [Fact]
    public void UnknownOrder_QuoteAndConfirmAreRefused()
    {
        var (afterQuote, quote) = OrderBook.Empty.Quote("R-404", Rules, PricingPolicies.Standard, "standard");
        var (afterConfirm, confirm) = OrderBook.Empty.Confirm("R-404", 1);

        Assert.Contains("does not exist", Assert.IsType<TransitionResult.Refused>(quote).Message);
        Assert.Contains("does not exist", Assert.IsType<TransitionResult.Refused>(confirm).Message);
        Assert.Same(OrderBook.Empty, afterQuote);
        Assert.Same(OrderBook.Empty, afterConfirm);
    }

    // REPEATED OPERATION
    [Fact]
    public void DuplicateSubmit_IsRefused_AndTheExistingOrderIsKept()
    {
        var (book, _) = OrderBook.Empty.Submit(Req("R-001", 2m, 1m, "First"));
        var (afterQuote, _) = book.Quote("R-001", Rules, PricingPolicies.Standard, "standard");

        var (same, result) = afterQuote.Submit(Req("R-001", 9m, 9m, "Second"));

        Assert.Contains("already exists", Assert.IsType<TransitionResult.Refused>(result).Message);
        Assert.Same(afterQuote, same);
        Assert.True(same.TryGet("R-001", out var state));
        Assert.Equal(840m, Assert.IsType<OrderState.Quoted>(state).Price); // not replaced by the duplicate
    }

    // STATE PRESERVATION
    [Fact]
    public void FailedTransition_LeavesBookAndStoredStateUnchanged()
    {
        var (book, _) = OrderBook.Empty.Submit(Req("R-001", 2m, 1m));
        Assert.True(book.TryGet("R-001", out var before));

        var (after, result) = book.Confirm("R-001", 1); // quote is missing

        Assert.IsType<TransitionResult.Refused>(result);
        Assert.Same(book, after);                       // no new book was created
        Assert.True(after.TryGet("R-001", out var stored));
        Assert.Same(before, stored);                    // the stored state is the very same object
        Assert.IsType<OrderState.Submitted>(stored);
    }

    [Fact]
    public void SuccessfulTransition_DoesNotMutateTheOriginalBook()
    {
        var (original, _) = OrderBook.Empty.Submit(Req("R-001", 2m, 1m));

        var (updated, _) = original.Quote("R-001", Rules, PricingPolicies.Standard, "standard");

        Assert.True(original.TryGet("R-001", out var oldState));
        Assert.True(updated.TryGet("R-001", out var newState));
        Assert.IsType<OrderState.Submitted>(oldState);
        Assert.IsType<OrderState.Quoted>(newState);
        Assert.NotSame(original, updated);
    }

    [Fact]
    public void OutsideLimits_QuoteRefused_OrderStaysSubmitted_AndCannotBeConfirmed()
    {
        var (book, _) = OrderBook.Empty.Submit(Req("R-004", 31m, 2m));

        var (afterQuote, quote) = book.Quote("R-004", Rules, PricingPolicies.Standard, "standard");
        var (afterConfirm, confirm) = afterQuote.Confirm("R-004", 1);

        Assert.IsType<TransitionResult.Refused>(quote);
        Assert.IsType<TransitionResult.Refused>(confirm);
        Assert.Same(book, afterConfirm);
        Assert.True(afterConfirm.TryGet("R-004", out var state));
        Assert.IsType<OrderState.Submitted>(state);
    }
}
