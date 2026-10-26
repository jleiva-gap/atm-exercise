namespace Atm.Domain;

public static class Money
{
    public const long Limit = 1_000_000_000;
    public const string Currency = "USD";

    public static Failure? ValidateAmount(long amount) =>
        amount is < 1 or > Limit
            ? new("invalid_amount", $"Amount must be between 1 and {Limit} cents.")
            : null;
}
