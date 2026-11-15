namespace DeliveryQuotes.Core;

/// <summary>Configuration of a priority score: how much one km and one kg count.</summary>
public sealed record ScoreConfig(decimal DistanceWeight, decimal WeightWeight)
{
    /// <summary>Long deliveries first.</summary>
    public static ScoreConfig DistanceFirst { get; } = new(2m, 0.5m);

    /// <summary>Heavy parcels first.</summary>
    public static ScoreConfig HeavyFirst { get; } = new(0.5m, 2m);

    public static Option<ScoreConfig> Find(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "distance-first" => Option.Some(DistanceFirst),
        "heavy-first" => Option.Some(HeavyFirst),
        _ => Option.None<ScoreConfig>(),
    };
}

/// <summary>Currying and partial application: Configuration -> (DomainItem -> Score).</summary>
public static class Scoring
{
    /// <summary>The plain three-argument function: score = km * distanceWeight + kg * weightWeight.</summary>
    public static decimal WeightedScore(decimal distanceWeight, decimal weightWeight, DeliveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.DistanceKm * distanceWeight + request.WeightKg * weightWeight;
    }

    /// <summary>
    /// CreateScore : ScoreConfig -> (DeliveryRequest -> decimal).
    /// The curried function receives its arguments in successive calls; fixing the first two (partial application)
    /// gives a configured function that only needs the request. No global setting is read or changed.
    /// </summary>
    public static Func<DeliveryRequest, decimal> CreateScore(ScoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Func<decimal, decimal, DeliveryRequest, decimal> weighted = WeightedScore;
        var curried = weighted.Curry();                    // decimal -> decimal -> DeliveryRequest -> decimal
        return curried(config.DistanceWeight)(config.WeightWeight);   // partial application: two arguments fixed
    }
}
