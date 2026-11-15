using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>
/// Closed set of outcomes of the factory (a discriminated union):
///   Valid   - carries the validated request;
///   Invalid - carries the reasons, and no request.
/// The private base constructor means no other case can be declared outside this type.
/// </summary>
public abstract record RequestValidation
{
    private RequestValidation()
    {
    }

    public sealed record Valid(DeliveryRequest Request) : RequestValidation;

    public sealed record Invalid(ImmutableArray<string> Reasons) : RequestValidation;
}
