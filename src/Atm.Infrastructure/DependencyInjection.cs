using Atm.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtmInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, string> databasePath
    )
    {
        services.AddDbContextFactory<AtmDbContext>(
            (provider, options) =>
            {
                var path = databasePath(provider);
                if (!Path.IsPathFullyQualified(path))
                    throw new InvalidOperationException(
                        "Atm:DatabasePath must be an absolute file path."
                    );
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    ForeignKeys = true,
                    DefaultTimeout = 5,
                    Pooling = false,
                }.ToString();
                options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(5));
            }
        );
        services.AddScoped<IAtmStore, SqliteAtmStore>();
        return services;
    }

    public static async Task InitializeAtmDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        var factory = services.GetRequiredService<IDbContextFactory<AtmDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
