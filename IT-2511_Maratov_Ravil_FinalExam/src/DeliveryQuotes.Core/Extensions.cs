using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>
/// The custom extension methods of the project. Each one is small, pure (no shared state is changed) and used by the application.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// EXTENSION 1 (reusable query operation).
    /// Receiver: IEnumerable&lt;T&gt;. Returns: IOrderedEnumerable&lt;T&gt;.
    /// Orders by a numeric key from the highest to the lowest and breaks ties by id, ordinally ascending, so the order is
    /// always the same for the same data. Belongs on IEnumerable&lt;T&gt; because every report ranks different item types the same way.
    /// Used by <see cref="Reports.DispatchList"/> and <see cref="Reports.CustomerWorkload"/>.
    /// </summary>
    public static IOrderedEnumerable<T> OrderByDescendingThenById<T>(
        this IEnumerable<T> source,
        Func<T, decimal> key,
        Func<T, string> id)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(id);

        return source.OrderByDescending(key).ThenBy(id, StringComparer.Ordinal);
    }

    /// <summary>
    /// EXTENSION 2 (composes Result values).
    /// Receiver: IEnumerable&lt;T&gt;. Returns: Result&lt;ImmutableArray&lt;U&gt;&gt;.
    /// Applies an operation that may fail to every item, in order. The first failure stops the loop (later items are never
    /// processed) and is returned; if all succeed, the values are returned in order. An empty source is a success with no values.
    /// Belongs on IEnumerable&lt;T&gt; because it works for any sequence. Used by <see cref="ReportingWorkflow.Build"/>
    /// to call the native library for every order: either all calls succeed or the whole report fails, with nothing partial.
    /// </summary>
    public static Result<ImmutableArray<U>> CollectResults<T, U>(this IEnumerable<T> source, Func<T, Result<U>> operation)
        where U : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operation);

        var values = ImmutableArray.CreateBuilder<U>();
        foreach (var item in source)
        {
            switch (operation(item))
            {
                case Result<U>.Ok ok:
                    values.Add(ok.Value);
                    break;
                case Result<U>.Failure failure:
                    return Result.Failure<ImmutableArray<U>>(failure.Error);
                default:
                    return Result.Failure<ImmutableArray<U>>(ErrorCode.Unexpected, "Unknown result case.");
            }
        }

        return Result.Success(values.ToImmutable());
    }

    /// <summary>
    /// EXTENSION 3 (currying).
    /// Receiver: Func&lt;A,B,C,R&gt;. Returns: Func&lt;A, Func&lt;B, Func&lt;C, R&gt;&gt;&gt;.
    /// Turns a function that takes three arguments at once into a chain of one-argument functions, so that
    /// arguments can be fixed one by one (partial application). Used by <see cref="Scoring.CreateScore"/>.
    /// </summary>
    public static Func<A, Func<B, Func<C, R>>> Curry<A, B, C, R>(this Func<A, B, C, R> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return a => b => c => function(a, b, c);
    }
}
