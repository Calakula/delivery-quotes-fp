namespace DeliveryQuotes.Core;

/// <summary>
/// Closed set of order states, each with its own data:
///   Submitted - the validated request only;
///   Quoted    - request + price + name of the policy that produced it;
///   Confirmed - everything in Quoted + the confirmation number.
/// Constructors are internal: states can only be reached through <see cref="OrderWorkflow"/> / <see cref="OrderBook"/>,
/// so "Confirmed without a quote" cannot be built by callers. Properties are get-only.
/// </summary>
public abstract record OrderState
{
    private OrderState()
    {
    }

    public sealed record Submitted : OrderState
    {
        internal Submitted(DeliveryRequest request)
        {
            Request = request;
        }

        public DeliveryRequest Request { get; }
    }

    public sealed record Quoted : OrderState
    {
        internal Quoted(DeliveryRequest request, decimal price, string policyName)
        {
            Request = request;
            Price = price;
            PolicyName = policyName;
        }

        public DeliveryRequest Request { get; }

        public decimal Price { get; }

        public string PolicyName { get; }
    }

    public sealed record Confirmed : OrderState
    {
        internal Confirmed(DeliveryRequest request, decimal price, string policyName, int confirmationNumber)
        {
            Request = request;
            Price = price;
            PolicyName = policyName;
            ConfirmationNumber = confirmationNumber;
        }

        public DeliveryRequest Request { get; }

        public decimal Price { get; }

        public string PolicyName { get; }

        public int ConfirmationNumber { get; }
    }
}
