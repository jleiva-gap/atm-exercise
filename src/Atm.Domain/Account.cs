namespace Atm.Domain;

public sealed class Account(int id, string name)
{
    public int Id { get; private set; } = id;
    public string Name { get; private set; } = name;
    public long BalanceMinor { get; private set; }

    public Failure? Deposit(long amount)
    {
        var failure = Money.ValidateAmount(amount) ?? CanCredit(amount);
        if (failure is not null)
            return failure;
        BalanceMinor = checked(BalanceMinor + amount);
        return null;
    }

    public Failure? Withdraw(long amount)
    {
        var failure = Money.ValidateAmount(amount) ?? CanDebit(amount);
        if (failure is not null)
            return failure;
        BalanceMinor = checked(BalanceMinor - amount);
        return null;
    }

    public static Failure? Transfer(Account source, Account destination, long amount)
    {
        if (source.Id == destination.Id)
            return new("invalid_accounts", "Transfer accounts must be different.");

        var failure =
            Money.ValidateAmount(amount)
            ?? source.CanDebit(amount)
            ?? destination.CanCredit(amount);
        if (failure is not null)
            return failure;

        // Check both sides before changing either account.
        source.BalanceMinor = checked(source.BalanceMinor - amount);
        destination.BalanceMinor = checked(destination.BalanceMinor + amount);
        return null;
    }

    private Failure? CanCredit(long amount) =>
        amount > Money.Limit - BalanceMinor
            ? new(
                "balance_limit_exceeded",
                "The destination account would exceed the balance limit."
            )
            : null;

    private Failure? CanDebit(long amount) =>
        amount > BalanceMinor
            ? new("insufficient_funds", "The source account has insufficient funds.")
            : null;
}
