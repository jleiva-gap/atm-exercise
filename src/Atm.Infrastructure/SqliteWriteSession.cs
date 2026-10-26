using Atm.Application;
using Atm.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Atm.Infrastructure;

internal sealed class SqliteWriteSession(AtmDbContext context, SqliteTransaction transaction)
    : IWriteSession
{
    public Task<BankTransaction?> FindByKeyAsync(Guid key, CancellationToken cancellationToken) =>
        SqliteErrors.HandleBusy(() =>
            context
                .Transactions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken)
        );

    public Task<Account?> FindAccountAsync(int id, CancellationToken cancellationToken) =>
        SqliteErrors.HandleBusy(() =>
            context.Accounts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        );

    public async Task CommitAsync(BankTransaction receipt, CancellationToken cancellationToken)
    {
        try
        {
            context.Transactions.Add(receipt);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (SqliteErrors.IsDuplicateKey(ex))
        {
            throw new DuplicateIdempotencyKeyException(ex);
        }
        catch (Exception ex) when (SqliteErrors.IsBusy(ex))
        {
            throw new DatabaseBusyException(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // SqliteTransaction.Dispose rolls back if Commit did not succeed.
        try
        {
            await transaction.DisposeAsync();
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
