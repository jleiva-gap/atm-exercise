using Atm.Domain;

namespace Atm.Application;

public sealed class TransactionService(IAtmStore store, TimeProvider clock)
{
    public async Task<Result<TransactionReceipt>> ExecuteAsync(
        TransactionCommand command,
        Guid key,
        CancellationToken cancellationToken
    )
    {
        if (command.Validate().Count != 0)
            return Result<TransactionReceipt>.Failed(
                new("invalid_request", "The transaction is invalid.")
            );

        try
        {
            await using var session = await store.BeginWriteAsync(cancellationToken);
            var existing = await session.FindByKeyAsync(key, cancellationToken);
            if (existing is not null)
                return Replay(command, existing);

            Account? source = null;
            Account? destination = null;
            if (command.SourceAccountId is int sourceId)
            {
                source = await session.FindAccountAsync(sourceId, cancellationToken);
                if (source is null)
                    return MissingAccount(sourceId);
            }
            if (command.DestinationAccountId is int destinationId)
            {
                destination = await session.FindAccountAsync(destinationId, cancellationToken);
                if (destination is null)
                    return MissingAccount(destinationId);
            }

            var failure = command.Type switch
            {
                TransactionType.Deposit => destination!.Deposit(command.AmountMinor),
                TransactionType.Withdrawal => source!.Withdraw(command.AmountMinor),
                TransactionType.Transfer => Account.Transfer(
                    source!,
                    destination!,
                    command.AmountMinor
                ),
                _ => throw new InvalidOperationException("Unvalidated transaction type."),
            };
            if (failure is not null)
                return Result<TransactionReceipt>.Failed(failure);

            var transaction = new BankTransaction(
                Guid.NewGuid(),
                key,
                command.Type,
                command.SourceAccountId,
                command.DestinationAccountId,
                command.AmountMinor,
                clock.GetUtcNow().UtcDateTime,
                source?.BalanceMinor,
                destination?.BalanceMinor
            );
            await session.CommitAsync(transaction, cancellationToken);
            return Result<TransactionReceipt>.Success(TransactionReceipt.From(transaction));
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // The write session has already rolled back/disposed. This read uses a fresh context.
            var existing = await store.FindByKeyAsync(key, cancellationToken);
            if (existing is null)
                throw;
            return Replay(command, existing);
        }
    }

    private static Result<TransactionReceipt> Replay(
        TransactionCommand command,
        BankTransaction transaction
    ) =>
        command.Matches(transaction)
            ? Result<TransactionReceipt>.Success(TransactionReceipt.From(transaction))
            : Result<TransactionReceipt>.Failed(
                new(
                    "idempotency_conflict",
                    "This Idempotency-Key was already used for a different transaction."
                )
            );

    private static Result<TransactionReceipt> MissingAccount(int id) =>
        Result<TransactionReceipt>.Failed(
            new("account_not_found", $"Account {id} does not exist.")
        );
}
