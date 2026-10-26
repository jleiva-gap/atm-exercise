using Atm.Domain;

namespace Atm.Application;

public interface IAtmStore
{
    Task<IWriteSession> BeginWriteAsync(CancellationToken cancellationToken);
    Task<BankTransaction?> FindByKeyAsync(Guid key, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(CancellationToken cancellationToken);
    Task<TransactionReceipt?> GetReceiptAsync(Guid id, CancellationToken cancellationToken);
    Task<HistoryPage?> GetHistoryAsync(
        int accountId,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    );
}

// Lifetime is the entire use case, including the first balance/key read.
// Disposal without a successful CommitAsync must roll back.
public interface IWriteSession : IAsyncDisposable
{
    Task<BankTransaction?> FindByKeyAsync(Guid key, CancellationToken cancellationToken);
    Task<Account?> FindAccountAsync(int id, CancellationToken cancellationToken);
    Task CommitAsync(BankTransaction transaction, CancellationToken cancellationToken);
}

public sealed class DatabaseBusyException(Exception inner)
    : Exception("The database is busy.", inner);

public sealed class DuplicateIdempotencyKeyException(Exception inner)
    : Exception("Duplicate idempotency key.", inner);
