using Atm.Application;
using Atm.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Atm.Infrastructure;

public sealed class SqliteAtmStore(IDbContextFactory<AtmDbContext> factory) : IAtmStore
{
    public Task<IWriteSession> BeginWriteAsync(CancellationToken cancellationToken) =>
        SqliteErrors.HandleBusy<IWriteSession>(async () =>
        {
            var context = await factory.CreateDbContextAsync(cancellationToken);
            SqliteTransaction? transaction = null;
            try
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
                var connection = (SqliteConnection)context.Database.GetDbConnection();
                // BEGIN IMMEDIATE, before the first key lookup or account read. SQLite owns the lock across processes.
                transaction = connection.BeginTransaction(deferred: false);
                await context.Database.UseTransactionAsync(transaction, cancellationToken);
                return new SqliteWriteSession(context, transaction);
            }
            catch
            {
                if (transaction is not null)
                    await transaction.DisposeAsync();
                await context.DisposeAsync();
                throw;
            }
        });

    public Task<BankTransaction?> FindByKeyAsync(Guid key, CancellationToken cancellationToken) =>
        SqliteErrors.HandleBusy(async () =>
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            return await context
                .Transactions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        });

    public Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(
        CancellationToken cancellationToken
    ) =>
        SqliteErrors.HandleBusy<IReadOnlyList<AccountSummary>>(async () =>
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            return await context
                .Accounts.AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new AccountSummary(x.Id, x.Name, Money.Currency, x.BalanceMinor))
                .ToListAsync(cancellationToken);
        });

    public Task<TransactionReceipt?> GetReceiptAsync(
        Guid id,
        CancellationToken cancellationToken
    ) =>
        SqliteErrors.HandleBusy(async () =>
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var transaction = await context
                .Transactions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return transaction is null ? null : TransactionReceipt.From(transaction);
        });

    public Task<HistoryPage?> GetHistoryAsync(
        int accountId,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    ) =>
        SqliteErrors.HandleBusy(async () =>
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            if (!await context.Accounts.AnyAsync(x => x.Id == accountId, cancellationToken))
                return null;
            // SQL OFFSET is 64-bit. Avoid int multiplication overflow and keep even very large valid pages well-defined.
            var offset = ((long)page - 1) * pageSize;
            var rows = await context
                .Transactions.FromSqlInterpolated(
                    $"""
                    SELECT * FROM Transactions
                    WHERE SourceAccountId = {accountId} OR DestinationAccountId = {accountId}
                    ORDER BY CreatedAtUtc DESC, Id DESC LIMIT {pageSize + 1} OFFSET {offset}
                    """
                )
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            return new HistoryPage(
                rows.Take(pageSize).Select(TransactionReceipt.From).ToArray(),
                page,
                pageSize,
                rows.Count > pageSize
            );
        });
}
