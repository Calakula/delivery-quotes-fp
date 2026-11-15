using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member A: the custom extension methods (OrderByDescendingThenById, CollectResults, Curry).
public class ExtensionTests
{
    [Fact]
    public void OrderByDescendingThenById_SortsByKeyThenOrdinalId_AndIsDeterministic()
    {
        var items = new[] { ("b", 5m), ("a", 5m), ("c", 9m), ("B", 5m) };
        var snapshot = items.ToArray();

        var ordered = items.OrderByDescendingThenById(x => x.Item2, x => x.Item1).Select(x => x.Item1).ToArray();
        var fromReversed = items.Reverse().OrderByDescendingThenById(x => x.Item2, x => x.Item1).Select(x => x.Item1).ToArray();

        Assert.Equal(new[] { "c", "B", "a", "b" }, ordered);   // 9 first, then the ties by ordinal id: "B" < "a" < "b"
        Assert.Equal(ordered, fromReversed);                    // the input order does not matter
        Assert.Equal(snapshot, items);                          // the source is not changed
    }

    [Fact]
    public void OrderByDescendingThenById_EmptyInput_GivesEmptyOrder()
    {
        Assert.Empty(Array.Empty<(string, decimal)>().OrderByDescendingThenById(x => x.Item2, x => x.Item1));
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<int>)null!).OrderByDescendingThenById(x => x, x => "id"));
    }

    [Fact]
    public void CollectResults_AllSucceed_ReturnsValuesInOrder_AndEmptyIsSuccess()
    {
        var ok = new[] { 1, 2, 3 }.CollectResults(x => Result.Success(x * 10));
        var empty = Array.Empty<int>().CollectResults(x => Result.Success(x));

        Assert.Equal(new[] { 10, 20, 30 }, Assert.IsType<Result<ImmutableArray<int>>.Ok>(ok).Value);
        Assert.Empty(Assert.IsType<Result<ImmutableArray<int>>.Ok>(empty).Value);
    }

    [Fact]
    public void CollectResults_FirstFailure_IsReturned_AndLaterItemsAreNeverProcessed()
    {
        var processed = new List<int>();

        var result = new[] { 1, 2, 3, 4 }.CollectResults(x =>
        {
            processed.Add(x);
            return x == 3 ? Result.Failure<int>(ErrorCode.InvalidInput, "three is rejected") : Result.Success(x);
        });

        Assert.Equal(new[] { 1, 2, 3 }, processed);   // item 4 was never touched
        var failure = Assert.IsType<Result<ImmutableArray<int>>.Failure>(result);
        Assert.Equal("three is rejected", failure.Error.Message);
    }

    [Fact]
    public void Curry_FixesArgumentsOneByOne_PartialApplicationGivesNewFunctions()
    {
        Func<int, int, int, int> combine = (a, b, c) => a * 100 + b * 10 + c;

        var curried = combine.Curry();
        var fixedTwo = curried(1)(2);   // partial application: the first two arguments are fixed

        Assert.Equal(123, curried(1)(2)(3));
        Assert.Equal(123, fixedTwo(3));
        Assert.Equal(124, fixedTwo(4));
        Assert.Equal(623, curried(6)(2)(3));   // another configuration does not disturb the first
        Assert.Equal(123, fixedTwo(3));
    }
}
