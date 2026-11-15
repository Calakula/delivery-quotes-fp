using System.Globalization;

namespace DeliveryQuotes.Core;

/// <summary>Display model of a quote for one request typed by the user.</summary>
public sealed record QuoteView(string Id, string Customer, decimal Price, decimal DiscountPercent, bool DiscountRecordFound, string PolicyName);

/// <summary>
/// Result chain A: raw text -> numbers -> validated request -> service limits -> price -> display model.
///   Bind is used where the next step can fail (parse, factory, limits, price check).
///   Map is used where the next step is a pure transformation (price to display model).
/// A failure at any step skips all later steps. No exception is used for these expected failures.
/// </summary>
public static class QuoteWorkflow
{
    private sealed record Priced(DeliveryRequest Request, decimal Price, decimal Percent, bool RecordFound);

    public static Result<QuoteView> QuoteOne(
        string? id,
        string? customer,
        string? distanceText,
        string? weightText,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        string policyName,
        DiscountTable discounts)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policyName);
        ArgumentNullException.ThrowIfNull(discounts);

        return RequestParser.ParseNumber("DistanceKm", distanceText)
            .Bind(distance => RequestParser.ParseNumber("WeightKg", weightText)
                .Bind(weight => DeliveryRequest.Create(id, customer, distance, weight).ToResult()))
            .Bind(request => CheckServiceLimits(request, rules))
            .Bind(request => Price(request, policy, discounts))
            .Map(priced => new QuoteView(priced.Request.Id, priced.Request.Customer, priced.Price, priced.Percent, priced.RecordFound, policyName));
    }

    private static Result<DeliveryRequest> CheckServiceLimits(DeliveryRequest request, EligibilityRules rules) =>
        rules.Allows(request)
            ? Result.Success(request)
            : Result.Failure<DeliveryRequest>(ErrorCode.OutsideServiceLimits, string.Create(CultureInfo.InvariantCulture,
                $"Order {request.Id} is outside the service limits ({rules.MaxDistanceKm} km, {rules.MaxWeightKg} kg)."));

    // Optional lookup: no discount record means "no discount" (documented); a record with 0 % is a record.
    private static Result<Priced> Price(DeliveryRequest request, Func<DeliveryRequest, decimal> policy, DiscountTable discounts)
    {
        var found = discounts.Find(request.Customer);
        var percent = found.GetValueOr(0m);
        var price = PricingPolicies.WithDiscount(policy, percent)(request);

        return price < 0m
            ? Result.Failure<Priced>(ErrorCode.InvalidInput, $"The pricing policy returned a negative price for {request.Id}.")
            : Result.Success(new Priced(request, price, percent, found is Option<decimal>.Some));
    }
}
