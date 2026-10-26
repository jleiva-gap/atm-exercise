using Atm.Domain;

namespace Atm.Application;

public sealed record TransactionCommand(
    TransactionType Type,
    int? SourceAccountId,
    int? DestinationAccountId,
    long AmountMinor
)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(Type))
            errors["type"] = ["Type must be deposit, withdrawal, or transfer."];
        if (Money.ValidateAmount(AmountMinor) is { } amountError)
            errors["amountMinor"] = [amountError.Detail];
        if (SourceAccountId is <= 0)
            errors["sourceAccountId"] = ["Account ID must be a positive integer."];
        if (DestinationAccountId is <= 0)
            errors["destinationAccountId"] = ["Account ID must be a positive integer."];
        if (
            Type is TransactionType.Withdrawal or TransactionType.Transfer
            && SourceAccountId is null
        )
            errors["sourceAccountId"] = ["A source account is required."];
        if (
            Type is TransactionType.Deposit or TransactionType.Transfer
            && DestinationAccountId is null
        )
            errors["destinationAccountId"] = ["A destination account is required."];
        if (Type == TransactionType.Deposit && SourceAccountId is not null)
            errors["sourceAccountId"] = ["A deposit cannot have a source account."];
        if (Type == TransactionType.Withdrawal && DestinationAccountId is not null)
            errors["destinationAccountId"] = ["A withdrawal cannot have a destination account."];
        if (
            Type == TransactionType.Transfer
            && SourceAccountId is not null
            && SourceAccountId == DestinationAccountId
        )
            errors["destinationAccountId"] = ["Transfer accounts must be different."];
        return errors;
    }

    public bool Matches(BankTransaction transaction) =>
        Type == transaction.Type
        && SourceAccountId == transaction.SourceAccountId
        && DestinationAccountId == transaction.DestinationAccountId
        && AmountMinor == transaction.AmountMinor;
}
