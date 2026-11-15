using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuotes.Core;

/// <summary>
/// Optional per-customer discount percentages (0 to 100). Immutable.
/// Lookup returns an <see cref="Option{T}"/>: Some(0) means "this customer has a record with 0 %",
/// None means "no record". They are different things and are never mixed up.
/// </summary>
public sealed class DiscountTable
{
    private readonly ImmutableDictionary<string, decimal> _percents;

    private DiscountTable(ImmutableDictionary<string, decimal> percents)
    {
        _percents = percents;
    }

    public static DiscountTable Empty { get; } =
        new(ImmutableDictionary.Create<string, decimal>(StringComparer.OrdinalIgnoreCase));

    public int Count => _percents.Count;

    /// <summary>Customer names are compared trimmed and case-insensitively.</summary>
    public Option<decimal> Find(string? customer)
    {
        if (string.IsNullOrWhiteSpace(customer))
        {
            return Option.None<decimal>();
        }

        return _percents.TryGetValue(customer.Trim(), out var percent)
            ? Option.Some(percent)
            : Option.None<decimal>();
    }

    /// <summary>Validated factory: all entries are checked before a table exists.</summary>
    public static Result<DiscountTable> Create(IEnumerable<KeyValuePair<string, decimal>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = ImmutableDictionary.CreateBuilder<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = (entry.Key ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return Result.Failure<DiscountTable>(ErrorCode.InvalidInput, "Discount table contains an empty customer name.");
            }
            if (entry.Value < 0m || entry.Value > 100m)
            {
                return Result.Failure<DiscountTable>(ErrorCode.InvalidInput,
                    string.Create(CultureInfo.InvariantCulture, $"Discount for '{name}' must be between 0 and 100 but was {entry.Value}."));
            }
            if (builder.ContainsKey(name))
            {
                return Result.Failure<DiscountTable>(ErrorCode.InvalidInput, $"Customer '{name}' appears more than once in the discount table.");
            }
            builder.Add(name, entry.Value);
        }

        return Result.Success(new DiscountTable(builder.ToImmutable()));
    }
}
