using Atm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Atm.Infrastructure;

public sealed class AtmDbContext(DbContextOptions<AtmDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<BankTransaction> Transactions => Set<BankTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Versioned schema is shared by the initial migration and snapshot. Keep it frozen;
        // future schema changes belong here and in a new migration/snapshot.
        Migrations.InitialModel.Build(modelBuilder);
    }
}
