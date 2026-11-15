using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>
/// SECOND DOMAIN MODEL (related to <see cref="DeliveryRequest"/> through the assignment workflow).
/// A courier with a daily capacity measured in effort points (the points calculated by the native library).
/// Like DeliveryRequest it can only be created through the validated factory: private constructor, get-only properties.
/// </summary>
public sealed class Courier
{
    public const int MaxCapacity = 10_000;

    private Courier(string id, string name, int capacity)
    {
        Id = id;
        Name = name;
        Capacity = capacity;
    }

    /// <summary>Trimmed, upper-case id (never empty).</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>Daily capacity in effort points, 0 to <see cref="MaxCapacity"/> (0 is legitimate: a courier who cannot take orders today).</summary>
    public int Capacity { get; }

    /// <summary>Validated factory. All broken rules are reported together; nothing is created when any rule fails.</summary>
    public static Result<Courier> Create(string? id, string? name, int capacity)
    {
        var normalizedId = DeliveryRequest.NormalizeId(id);
        var normalizedName = (name ?? string.Empty).Trim();
        var reasons = ImmutableArray.CreateBuilder<string>();

        if (normalizedId.Length == 0)
        {
            reasons.Add("Courier id is empty.");
        }
        if (normalizedName.Length == 0)
        {
            reasons.Add("Courier name is empty.");
        }
        if (capacity < 0 || capacity > MaxCapacity)
        {
            reasons.Add($"Courier capacity must be between 0 and {MaxCapacity} points but was {capacity}.");
        }

        return reasons.Count > 0
            ? Result.Failure<Courier>(ErrorCode.InvalidInput, string.Join(" ", reasons))
            : Result.Success(new Courier(normalizedId, normalizedName, capacity));
    }
}
