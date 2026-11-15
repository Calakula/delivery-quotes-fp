namespace DeliveryQuotes.Core;

/// <summary>
/// Pure transitions between order states. Each takes the CURRENT state (and explicit inputs) and returns a
/// <see cref="TransitionResult"/>. Nothing is mutated, nothing is read from outside, and the prerequisite of every
/// step is visible in its signature: Quote needs a Submitted state, Confirm needs a Quoted state.
///
/// Documented rules for repeated or wrong-order calls:
///   Quote   on Quoted / Confirmed -> Refused (a quote is fixed once issued).
///   Confirm on Confirmed          -> Moved with the EXISTING confirmation (idempotent, number unchanged).
///   Confirm on Submitted          -> Refused (no quote yet).
/// </summary>
public static class OrderWorkflow
{
    public static TransitionResult Quote(
        OrderState state,
        EligibilityRules rules,
        Func<DeliveryRequest, decimal> policy,
        string policyName)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policyName);

        switch (state)
        {
            case OrderState.Submitted submitted when !rules.Allows(submitted.Request):
                return TransitionResult.Refuse(ErrorCode.OutsideServiceLimits, Inv(
                    $"Order {submitted.Request.Id} is outside the service limits ({rules.MaxDistanceKm} km, {rules.MaxWeightKg} kg) and cannot be quoted."));

            case OrderState.Submitted submitted:
            {
                var price = policy(submitted.Request);
                return price < 0m
                    ? TransitionResult.Refuse(ErrorCode.InvalidInput, $"Policy '{policyName}' returned a negative price for {submitted.Request.Id}.")
                    : new TransitionResult.Moved(new OrderState.Quoted(submitted.Request, price, policyName));
            }

            case OrderState.Quoted quoted:
                return TransitionResult.Refuse(ErrorCode.InvalidState, Inv(
                    $"Order {quoted.Request.Id} is already quoted at {quoted.Price:0} KZT; a quote cannot be changed."));

            case OrderState.Confirmed confirmed:
                return TransitionResult.Refuse(ErrorCode.InvalidState, $"Order {confirmed.Request.Id} is already confirmed; it cannot be quoted again.");

            default:
                return TransitionResult.Refuse(ErrorCode.Unexpected, "Unknown order state; quote refused.");
        }
    }

    public static TransitionResult Confirm(OrderState state, int confirmationNumber)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (confirmationNumber <= 0)
        {
            return TransitionResult.Refuse(ErrorCode.InvalidInput, Inv($"Confirmation number must be positive but was {confirmationNumber}."));
        }

        switch (state)
        {
            case OrderState.Quoted quoted:
                return new TransitionResult.Moved(
                    new OrderState.Confirmed(quoted.Request, quoted.Price, quoted.PolicyName, confirmationNumber));

            case OrderState.Confirmed confirmed:
                return new TransitionResult.Moved(confirmed); // repeated confirmation: the existing outcome is returned

            case OrderState.Submitted submitted:
                return TransitionResult.Refuse(ErrorCode.InvalidState, $"Order {submitted.Request.Id} has no quote yet; quote it before confirming.");

            default:
                return TransitionResult.Refuse(ErrorCode.Unexpected, "Unknown order state; confirmation refused.");
        }
    }

    /// <summary>The validated request inside any state (every case carries one).</summary>
    public static DeliveryRequest RequestOf(OrderState state) => state switch
    {
        OrderState.Submitted s => s.Request,
        OrderState.Quoted q => q.Request,
        OrderState.Confirmed c => c.Request,
        _ => throw new InvalidOperationException($"Unhandled order state {state?.GetType().Name ?? "null"}."),
    };

    /// <summary>One line of text for a state; every case is matched explicitly, an unknown case is an error.</summary>
    public static string Describe(OrderState state) => state switch
    {
        OrderState.Submitted s => Inv($"{s.Request.Id}: submitted ({s.Request.DistanceKm} km, {s.Request.WeightKg} kg), waiting for a quote"),
        OrderState.Quoted q => Inv($"{q.Request.Id}: quoted {q.Price:0} KZT ({q.PolicyName}), waiting for confirmation"),
        OrderState.Confirmed c => Inv($"{c.Request.Id}: confirmed #{c.ConfirmationNumber}, {c.Price:0} KZT ({c.PolicyName})"),
        _ => throw new InvalidOperationException($"Unhandled order state {state?.GetType().Name ?? "null"}."),
    };

    private static string Inv(FormattableString text) => FormattableString.Invariant(text);
}
