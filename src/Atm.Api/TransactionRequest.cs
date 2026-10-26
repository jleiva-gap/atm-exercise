using Atm.Application;
using Atm.Domain;

namespace Atm.Api;

public sealed record TransactionRequest(
    string? Type,
    int? SourceAccountId,
    int? DestinationAccountId,
    long? AmountMinor
)
{
    public TransactionCommand ToCommand() =>
        new(
            Type switch
            {
                "deposit" => TransactionType.Deposit,
                "withdrawal" => TransactionType.Withdrawal,
                "transfer" => TransactionType.Transfer,
                _ => (TransactionType)(-1),
            },
            SourceAccountId,
            DestinationAccountId,
            AmountMinor ?? 0
        );
}
