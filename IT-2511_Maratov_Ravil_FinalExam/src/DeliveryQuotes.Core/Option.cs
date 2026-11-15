namespace DeliveryQuotes.Core;

/// <summary>
/// A lookup outcome: Some carries the found value, None means "not there".
/// Presence is a case, never a property of the value: Some(0), Some(false) and Some("") are legitimate found values.
/// </summary>
public abstract record Option<T> where T : notnull
{
    private Option()
    {
    }

    public sealed record Some : Option<T>
    {
        internal Some(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record None : Option<T>
    {
        internal None()
        {
        }
    }
}

public static class Option
{
    public static Option<T> Some<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Option<T>.Some(value);
    }

    public static Option<T> None<T>() where T : notnull => new Option<T>.None();
}

public static class OptionExtensions
{
    /// <summary>Transforms the found value; None stays None and the function is not called.</summary>
    public static Option<U> Map<T, U>(this Option<T> option, Func<T, U> map) where T : notnull where U : notnull
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(map);

        return option switch
        {
            Option<T>.Some some => Option.Some(map(some.Value)),
            Option<T>.None => Option.None<U>(),
            _ => throw new InvalidOperationException("Unknown option case."),
        };
    }

    /// <summary>For a REQUIRED item: the missing case becomes a meaningful failure.</summary>
    public static Result<T> ToResult<T>(this Option<T> option, Func<Error> ifMissing) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(ifMissing);

        return option switch
        {
            Option<T>.Some some => Result.Success(some.Value),
            Option<T>.None => Result.Failure<T>(ifMissing()),
            _ => Result.Failure<T>(ErrorCode.Unexpected, "Unknown option case."),
        };
    }

    /// <summary>For an OPTIONAL item: an explicit, documented fallback used only when the value is absent.</summary>
    public static T GetValueOr<T>(this Option<T> option, T fallback) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(fallback);

        return option switch
        {
            Option<T>.Some some => some.Value,
            Option<T>.None => fallback,
            _ => throw new InvalidOperationException("Unknown option case."),
        };
    }
}
