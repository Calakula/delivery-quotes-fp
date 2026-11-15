using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuotes.Core;

/// <summary>
/// A VALIDATED delivery request. The only way to obtain one is <see cref="Create"/>:
/// the constructor is private and every property is get-only (no setter, no init), so callers
/// cannot build an unchecked instance or change a checked one afterwards.
/// It is a class, not a record, on purpose: a record would offer a public "with" copy that could be re-initialised.
/// </summary>
public sealed class DeliveryRequest
{
    private DeliveryRequest(string id, string customer, decimal distanceKm, decimal weightKg)
    {
        Id = id;
        Customer = customer;
        DistanceKm = distanceKm;
        WeightKg = weightKg;
    }

    /// <summary>Trimmed, upper-case id (never empty).</summary>
    public string Id { get; }

    /// <summary>Trimmed customer name (never empty).</summary>
    public string Customer { get; }

    /// <summary>Distance in km, always greater than 0.</summary>
    public decimal DistanceKm { get; }

    /// <summary>Weight in kg, never negative (0 is allowed).</summary>
    public decimal WeightKg { get; }

    /// <summary>The single rule for turning free text into an order id; also used when looking orders up.</summary>
    public static string NormalizeId(string? id) => (id ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Validated factory. Returns <see cref="RequestValidation.Valid"/> with a usable request, or
    /// <see cref="RequestValidation.Invalid"/> with ALL reasons found. A partly built request never escapes.
    /// </summary>
    public static RequestValidation Create(string? id, string? customer, decimal distanceKm, decimal weightKg)
    {
        var normalizedId = NormalizeId(id);
        var normalizedCustomer = (customer ?? string.Empty).Trim();
        var reasons = ImmutableArray.CreateBuilder<string>();

        if (normalizedId.Length == 0)
        {
            reasons.Add("Id is empty.");
        }
        if (normalizedCustomer.Length == 0)
        {
            reasons.Add("Customer is empty.");
        }
        if (distanceKm <= 0m)
        {
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"DistanceKm must be greater than 0 but was {distanceKm}."));
        }
        if (weightKg < 0m)
        {
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"WeightKg must not be negative but was {weightKg}."));
        }

        if (reasons.Count > 0)
        {
            return new RequestValidation.Invalid(reasons.ToImmutable());
        }

        return new RequestValidation.Valid(new DeliveryRequest(normalizedId, normalizedCustomer, distanceKm, weightKg));
    }
}
