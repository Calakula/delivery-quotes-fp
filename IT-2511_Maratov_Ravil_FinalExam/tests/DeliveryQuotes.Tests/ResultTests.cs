using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member A: Result, Option, Map, Bind and the adapters.
public class ResultTests
{
    private static readonly Error Boom = new(ErrorCode.NotFound, "Order 'X' does not exist.");

    [Fact]
    public void Success_And_Failure_AreDistinctCases_WithTheirOwnPayload()
    {
        var ok = Assert.IsType<Result<int>.Ok>(Result.Success(42));
        var failure = Assert.IsType<Result<int>.Failure>(Result.Failure<int>(Boom));

        Assert.Equal(42, ok.Value);
        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
        Assert.Contains("'X'", failure.Error.Message);
    }

    [Fact]
    public void ContradictoryStates_CannotBeBuilt()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));   // success without a payload
        Assert.Throws<ArgumentNullException>(() => Result.Failure<int>(null!));      // failure without an error
        Assert.Throws<ArgumentNullException>(() => Option.Some<string>(null!));
        Assert.Empty(typeof(Result<int>.Ok).GetConstructors());
        Assert.Empty(typeof(Result<int>.Failure).GetConstructors());
        Assert.Empty(typeof(Option<int>.Some).GetConstructors());
    }

    // MAP SUCCESS
    [Fact]
    public void Map_Success_PassesTheValueToThePureFunction_AndReturnsTheNewValue()
    {
        var received = new List<int>();

        var mapped = Result.Success(21).Map(x =>
        {
            received.Add(x);
            return (x * 2).ToString();
        });

        Assert.Equal(new[] { 21 }, received);
        Assert.Equal("42", Assert.IsType<Result<string>.Ok>(mapped).Value);
    }

    // BIND SUCCESS
    [Fact]
    public void Bind_Success_PassesTheValueOn_AndKeepsTheResultOfTheNextOperation()
    {
        var received = new List<int>();

        var ok = Result.Success(10).Bind(x =>
        {
            received.Add(x);
            return Result.Success(x + 5);
        });
        var rejected = Result.Success(10).Bind(x => Result.Failure<int>(ErrorCode.InvalidState, "not allowed"));

        Assert.Equal(new[] { 10 }, received);
        Assert.Equal(15, Assert.IsType<Result<int>.Ok>(ok).Value);
        var failure = Assert.IsType<Result<int>.Failure>(rejected);   // the failure returned by the next step is preserved
        Assert.Equal(ErrorCode.InvalidState, failure.Error.Code);
    }

    // FAILURE PROPAGATION: the supplied functions are not executed
    [Fact]
    public void Map_Failure_IsPropagated_AndTheFunctionIsNotCalled()
    {
        var calls = 0;

        var result = Result.Failure<int>(Boom).Map(x =>
        {
            calls++;
            return x + 1;
        });

        Assert.Equal(0, calls);
        Assert.Equal(Boom, Assert.IsType<Result<int>.Failure>(result).Error);
    }

    [Fact]
    public void Bind_Failure_IsPropagated_AndTheFunctionIsNotCalled()
    {
        var calls = 0;

        var result = Result.Failure<int>(Boom).Bind(x =>
        {
            calls++;
            return Result.Success(x + 1);
        });

        Assert.Equal(0, calls);
        Assert.Equal(Boom, Assert.IsType<Result<int>.Failure>(result).Error);
    }

    [Fact]
    public void LongChain_FirstFailureSkipsEveryLaterStep()
    {
        var calls = new List<string>();

        var result = Result.Success(1)
            .Bind(x => { calls.Add("first"); return Result.Failure<int>(ErrorCode.InvalidInput, "stop here"); })
            .Map(x => { calls.Add("second"); return x + 1; })
            .Bind(x => { calls.Add("third"); return Result.Success(x + 1); });

        Assert.Equal(new[] { "first" }, calls);
        Assert.Equal("stop here", Assert.IsType<Result<int>.Failure>(result).Error.Message);
    }

    [Fact]
    public void Match_HandlesBothCasesExplicitly()
    {
        Assert.Equal("ok 5", Result.Success(5).Match(v => $"ok {v}", e => $"error {e.Code}"));
        Assert.Equal("error NotFound", Result.Failure<int>(Boom).Match(v => $"ok {v}", e => $"error {e.Code}"));
    }

    // LOOKUP PRESENT AND ABSENT: legitimate primitive values are not absence
    [Fact]
    public void Option_ZeroFalseAndEmptyString_AreFoundValues_NotAbsence()
    {
        Assert.Equal(0, Assert.IsType<Option<int>.Some>(Option.Some(0)).Value);
        Assert.False(Assert.IsType<Option<bool>.Some>(Option.Some(false)).Value);
        Assert.Equal("", Assert.IsType<Option<string>.Some>(Option.Some("")).Value);
        Assert.IsType<Option<int>.None>(Option.None<int>());
    }

    [Fact]
    public void Option_ToResult_And_GetValueOr_HandleBothCases()
    {
        var missing = Error("missing");

        Assert.Equal(0, Assert.IsType<Result<int>.Ok>(Option.Some(0).ToResult(() => missing)).Value);
        Assert.Equal(missing, Assert.IsType<Result<int>.Failure>(Option.None<int>().ToResult(() => missing)).Error);
        Assert.Equal(0, Option.Some(0).GetValueOr(99));      // present zero is kept, the fallback is not used
        Assert.Equal(99, Option.None<int>().GetValueOr(99)); // absent value gets the documented fallback
        Assert.IsType<Option<string>.None>(Option.None<int>().Map(x => x.ToString()));
    }

    private static Error Error(string message) => new(ErrorCode.NotFound, message);

    // The validation outcome and the transition outcome keep their own meaning and convert on purpose
    [Fact]
    public void Adapters_ConvertValidationAndTransitionOutcomesToResults()
    {
        var valid = DeliveryRequest.Create("R-1", "Dana", 4m, 2m).ToResult();
        var invalid = DeliveryRequest.Create("", "Dana", 4m, 2m).ToResult();
        Assert.Equal("R-1", Assert.IsType<Result<DeliveryRequest>.Ok>(valid).Value.Id);
        Assert.Equal(ErrorCode.InvalidRequest, Assert.IsType<Result<DeliveryRequest>.Failure>(invalid).Error.Code);

        var (_, moved) = OrderBook.Empty.Submit(Req("R-1", 4m, 2m));
        var (_, refused) = OrderBook.Empty.Confirm("R-1", 1);
        Assert.IsType<OrderState.Submitted>(Assert.IsType<Result<OrderState>.Ok>(moved.ToResult()).Value);
        Assert.Equal(ErrorCode.NotFound, Assert.IsType<Result<OrderState>.Failure>(refused.ToResult()).Error.Code);
    }
}
