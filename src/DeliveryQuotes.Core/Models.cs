using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>A validated delivery request (domain data, never raw text).</summary>
public sealed record DeliveryRequest(string Id, string Customer, decimal DistanceKm, decimal WeightKg);

/// <summary>Service limits used by Stage 1. Passed explicitly, so nothing is hidden in global state.</summary>
public sealed record EligibilityRules(decimal MaxDistanceKm, decimal MaxWeightKg)
{
    /// <summary>Courier covers up to 30 km (inclusive) and parcels up to 20 kg (inclusive).</summary>
    public static EligibilityRules Default { get; } = new(30m, 20m);
}

/// <summary>Stage 2 output: a request together with its computed price.</summary>
public sealed record PricedRequest(DeliveryRequest Request, decimal Price);

/// <summary>One line of the final result.</summary>
public sealed record Quote(string Id, string Customer, decimal Price);

/// <summary>Stage 3 output: ordered quotes, their total and how many requests were not eligible.</summary>
public sealed record QuoteSummary(ImmutableArray<Quote> Quotes, decimal TotalPrice, int SkippedCount);

/// <summary>Result of parsing a file: valid requests plus one message per rejected line.</summary>
public sealed record ParseReport(ImmutableArray<DeliveryRequest> Requests, ImmutableArray<string> Errors)
{
    public bool IsValid => Errors.IsEmpty;
}
