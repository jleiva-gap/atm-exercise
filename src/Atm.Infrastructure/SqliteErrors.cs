using Atm.Application;
using Microsoft.Data.Sqlite;

namespace Atm.Infrastructure;

internal static class SqliteErrors
{
    public static SqliteException? Find(Exception exception) =>
        exception as SqliteException
        ?? (exception.InnerException is { } inner ? Find(inner) : null);

    public static bool IsBusy(Exception exception) => Find(exception)?.SqliteErrorCode is 5 or 6;

    public static bool IsDuplicateKey(Exception exception) =>
        Find(exception) is { SqliteExtendedErrorCode: 2067 } sqlite
        && sqlite.Message.Contains(
            "UNIQUE constraint failed: Transactions.IdempotencyKey",
            StringComparison.Ordinal
        );

    public static async Task<T> HandleBusy<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (IsBusy(ex))
        {
            throw new DatabaseBusyException(ex);
        }
    }
}
