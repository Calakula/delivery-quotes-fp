using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>
/// Outcome of a state transition:
///   Moved   - carries the NEW state;
///   Refused - carries an error code and the reasons; the old state stays valid and unchanged.
/// <see cref="ResultExtensions.ToResult(TransitionResult)"/> turns it into a Result so it can be chained with Bind.
/// </summary>
public abstract record TransitionResult
{
    private TransitionResult()
    {
    }

    public sealed record Moved(OrderState Next) : TransitionResult;

    public sealed record Refused(ErrorCode Code, ImmutableArray<string> Reasons) : TransitionResult
    {
        public string Message => string.Join(" ", Reasons);
    }

    public static TransitionResult Refuse(ErrorCode code, string reason) => new Refused(code, ImmutableArray.Create(reason));
}
