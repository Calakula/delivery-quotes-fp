using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member B: result chain A (text -> validated request -> limits -> price -> display model).
public class QuoteWorkflowTests
{
    private static Result<QuoteView> Quote(string id, string customer, string km, string kg, DiscountTable? discounts = null, Func<DeliveryRequest, decimal>? policy = null) =>
        QuoteWorkflow.QuoteOne(id, customer, km, kg, EligibilityRules.Default, policy ?? PricingPolicies.Standard, "standard", discounts ?? DiscountTable.Empty);

    private static DiscountTable Discounts(params (string Name, decimal Percent)[] entries) =>
        Assert.IsType<Result<DiscountTable>.Ok>(
            DiscountTable.Create(entries.Select(e => new KeyValuePair<string, decimal>(e.Name, e.Percent)))).Value;

    // COMPLETE SUCCESS
    [Fact]
    public void CompleteChain_ValidInput_ProducesTheDocumentedQuote()
    {
        var quote = Assert.IsType<Result<QuoteView>.Ok>(Quote("r-001", "Aigerim", "2", "1")).Value;

        Assert.Equal("R-001", quote.Id);
        Assert.Equal(840m, quote.Price);          // 600 + 2 * 120
        Assert.Equal(0m, quote.DiscountPercent);
        Assert.False(quote.DiscountRecordFound);  // no record: no discount, and the quote says so
    }

    [Fact]
    public void Discount_RecordApplied_ZeroRecordIsStillARecord_AbsentMeansNoDiscount()
    {
        var table = Discounts(("Aigerim", 10m), ("Dana", 0m));

        var withDiscount = Assert.IsType<Result<QuoteView>.Ok>(Quote("R-001", "Aigerim", "2", "1", table)).Value;
        var zeroRecord = Assert.IsType<Result<QuoteView>.Ok>(Quote("R-002", "Dana", "10", "5", table)).Value;
        var absent = Assert.IsType<Result<QuoteView>.Ok>(Quote("R-009", "Timur", "5", "2", table)).Value;

        Assert.Equal(756m, withDiscount.Price);   // 840 - 10 %
        Assert.True(withDiscount.DiscountRecordFound);
        Assert.Equal(1800m, zeroRecord.Price);    // 0 % changes nothing, but the record exists
        Assert.True(zeroRecord.DiscountRecordFound);
        Assert.Equal(0m, zeroRecord.DiscountPercent);
        Assert.Equal(1200m, absent.Price);
        Assert.False(absent.DiscountRecordFound);
    }

    // DOMAIN FAILURES: each has its own code and a useful message
    [Fact]
    public void DomainFailures_EachHasItsCodeAndNamesTheRuleOrItem()
    {
        var invalidQuantity = Assert.IsType<Result<QuoteView>.Failure>(Quote("R-1", "Dana", "-3", "1")).Error;
        var notANumber = Assert.IsType<Result<QuoteView>.Failure>(Quote("R-1", "Dana", "abc", "1")).Error;
        var outside = Assert.IsType<Result<QuoteView>.Failure>(Quote("R-4", "Aruzhan", "31", "2")).Error;

        Assert.Equal(ErrorCode.InvalidRequest, invalidQuantity.Code);
        Assert.Contains("DistanceKm must be greater than 0", invalidQuantity.Message);
        Assert.Equal(ErrorCode.InvalidInput, notANumber.Code);
        Assert.Contains("'abc'", notANumber.Message);
        Assert.Equal(ErrorCode.OutsideServiceLimits, outside.Code);
        Assert.Contains("R-4", outside.Message);
    }

    // FAILURE PROPAGATION in the real chain: the price policy is never called after an earlier failure
    [Fact]
    public void Failure_StopsTheChain_ThePricingPolicyIsNotCalled()
    {
        var policyCalls = 0;
        decimal CountingPolicy(DeliveryRequest r)
        {
            policyCalls++;
            return 100m;
        }

        Assert.IsType<Result<QuoteView>.Failure>(Quote("R-1", "Dana", "-3", "1", null, CountingPolicy));   // invalid request
        Assert.IsType<Result<QuoteView>.Failure>(Quote("R-1", "Dana", "31", "1", null, CountingPolicy));   // outside limits
        Assert.IsType<Result<QuoteView>.Failure>(Quote("R-1", "Dana", "abc", "1", null, CountingPolicy));  // not a number
        Assert.Equal(0, policyCalls);

        Assert.IsType<Result<QuoteView>.Ok>(Quote("R-1", "Dana", "5", "1", null, CountingPolicy));
        Assert.Equal(1, policyCalls);   // called exactly once on success
    }

    [Fact]
    public void Boundary_ExactLimitIsQuoted_JustAboveFails_MissingValuesAreInvalidInput()
    {
        Assert.IsType<Result<QuoteView>.Ok>(Quote("R-1", "Dana", "30", "20"));
        Assert.IsType<Result<QuoteView>.Failure>(Quote("R-1", "Dana", "30.01", "20"));
        Assert.IsType<Result<QuoteView>.Failure>(Quote("R-1", "Dana", "30", "20.01"));
        Assert.Equal(ErrorCode.InvalidInput,
            Assert.IsType<Result<QuoteView>.Failure>(QuoteWorkflow.QuoteOne("R-1", "Dana", null, "1", EligibilityRules.Default, PricingPolicies.Standard, "standard", DiscountTable.Empty)).Error.Code);
    }

    [Fact]
    public void TwoPolicies_SameInput_GiveDifferentQuotes()
    {
        var standard = Assert.IsType<Result<QuoteView>.Ok>(Quote("R-001", "Aigerim", "2", "1")).Value;
        var express = Assert.IsType<Result<QuoteView>.Ok>(Quote("R-001", "Aigerim", "2", "1", null, PricingPolicies.Express)).Value;

        Assert.Equal(840m, standard.Price);
        Assert.Equal(1360m, express.Price);
    }
}
