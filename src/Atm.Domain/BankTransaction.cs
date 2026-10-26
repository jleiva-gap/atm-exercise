namespace Atm.Domain;

public sealed record BankTransaction(
    Guid Id,
    Guid IdempotencyKey,
    TransactionType Type,
    int? SourceAccountId,
    int? DestinationAccountId,
    long AmountMinor,
    DateTime CreatedAtUtc,
    long? SourceBalanceAfterMinor,
    long? DestinationBalanceAfterMinor
);
