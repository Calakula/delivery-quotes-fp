namespace DeliveryQuotes.Core;

/// <summary>
/// An explicit outcome that is either a success carrying a value or a failure carrying an <see cref="Error"/>.
/// Contradictory states cannot be built: the base constructor is private, the case constructors are internal,
/// <see cref="Result.Success{T}"/> refuses a null value and <see cref="Result.Failure{T}(Error)"/> refuses a null error.
/// </summary>
public abstract record Result<T> where T : notnull
{
    private Result()
    {
    }

    public sealed record Ok : Result<T>
    {
        internal Ok(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record Failure : Result<T>
    {
        internal Failure(Error error)
        {
            Error = error;
        }

        public Error Error { get; }
    }
}

/// <summary>The only way for callers to create results.</summary>
public static class Result
{
    public static Result<T> Success<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>.Ok(value);
    }

    public static Result<T> Failure<T>(Error error) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>.Failure(error);
    }

    public static Result<T> Failure<T>(ErrorCode code, string message) where T : notnull =>
        Failure<T>(new Error(code, message));
}
