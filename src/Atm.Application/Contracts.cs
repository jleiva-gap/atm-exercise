using Atm.Domain;

namespace Atm.Application;

public sealed record AccountSummary(int Id, string Name, string Currency, long BalanceMinor);

public sealed record TransactionReceipt(
    Guid Id,
    string Type,
    int? SourceAccountId,
    int? DestinationAccountId,
    long AmountMinor,
    string Currency,
    DateTime CreatedAtUtc,
    long? SourceBalanceAfterMinor,
    long? DestinationBalanceAfterMinor
)
{
    public static TransactionReceipt From(BankTransaction transaction) =>
        new(
            transaction.Id,
            transaction.Type.ToString().ToLowerInvariant(),
            transaction.SourceAccountId,
            transaction.DestinationAccountId,
            transaction.AmountMinor,
            Money.Currency,
            transaction.CreatedAtUtc,
            transaction.SourceBalanceAfterMinor,
            transaction.DestinationBalanceAfterMinor
        );
}

public sealed record HistoryPage(
    IReadOnlyList<TransactionReceipt> Items,
    int Page,
    int PageSize,
    bool HasMore
);

public sealed record Result<T>
{
    private Result(T? value, Failure? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public Failure? Error { get; }

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failed(Failure error) => new(default, error);
}
