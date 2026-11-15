namespace DeliveryQuotes.Core;

/// <summary>Map, Bind and the adapters from the Assignment 6 outcomes.</summary>
public static class ResultExtensions
{
    /// <summary>
    /// Map : Result&lt;T&gt; + Func&lt;T,U&gt; -> Result&lt;U&gt;.
    /// The function is pure and returns a plain U. On failure it is NOT called and the same error is passed on.
    /// </summary>
    public static Result<U> Map<T, U>(this Result<T> result, Func<T, U> map) where T : notnull where U : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);

        switch (result)
        {
            case Result<T>.Ok ok:
                return Result.Success(map(ok.Value));
            case Result<T>.Failure failure:
                return Result.Failure<U>(failure.Error);
            default:
                return Result.Failure<U>(ErrorCode.Unexpected, "Unknown result case.");
        }
    }

    /// <summary>
    /// Bind : Result&lt;T&gt; + Func&lt;T,Result&lt;U&gt;&gt; -> Result&lt;U&gt;.
    /// The function itself may fail and returns a Result&lt;U&gt;, which is kept as it is.
    /// On failure it is NOT called and the same error is passed on.
    /// </summary>
    public static Result<U> Bind<T, U>(this Result<T> result, Func<T, Result<U>> bind) where T : notnull where U : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);

        switch (result)
        {
            case Result<T>.Ok ok:
                return bind(ok.Value);
            case Result<T>.Failure failure:
                return Result.Failure<U>(failure.Error);
            default:
                return Result.Failure<U>(ErrorCode.Unexpected, "Unknown result case.");
        }
    }

    /// <summary>Handles both cases explicitly in one place (used at the application boundary).</summary>
    public static R Match<T, R>(this Result<T> result, Func<T, R> onSuccess, Func<Error, R> onFailure) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return result switch
        {
            Result<T>.Ok ok => onSuccess(ok.Value),
            Result<T>.Failure failure => onFailure(failure.Error),
            _ => onFailure(new Error(ErrorCode.Unexpected, "Unknown result case.")),
        };
    }

    /// <summary>
    /// A validation outcome and a Result mean different things: RequestValidation answers "is this input acceptable?",
    /// Result answers "did this operation succeed?". Invalid becomes a failure with code InvalidRequest.
    /// </summary>
    public static Result<DeliveryRequest> ToResult(this RequestValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return validation switch
        {
            RequestValidation.Valid valid => Result.Success(valid.Request),
            RequestValidation.Invalid invalid => Result.Failure<DeliveryRequest>(ErrorCode.InvalidRequest, string.Join(" ", invalid.Reasons)),
            _ => Result.Failure<DeliveryRequest>(ErrorCode.Unexpected, "Unknown validation outcome."),
        };
    }

    /// <summary>
    /// A transition outcome carries a state change: Moved gives the new state, Refused keeps its code and reasons.
    /// </summary>
    public static Result<OrderState> ToResult(this TransitionResult transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        return transition switch
        {
            TransitionResult.Moved moved => Result.Success(moved.Next),
            TransitionResult.Refused refused => Result.Failure<OrderState>(refused.Code, refused.Message),
            _ => Result.Failure<OrderState>(ErrorCode.Unexpected, "Unknown transition outcome."),
        };
    }
}
