using Atm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atm.Infrastructure.Migrations;

internal static class InitialModel
{
    public static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable(
                "Accounts",
                table =>
                {
                    table.HasCheckConstraint("CK_Accounts_Id", "Id IN (1, 2)");
                    table.HasCheckConstraint(
                        "CK_Accounts_Balance",
                        "typeof(BalanceMinor) = 'integer' AND BalanceMinor BETWEEN 0 AND 1000000000"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever().HasColumnType("INTEGER");
            b.Property(x => x.Name).IsRequired().HasColumnType("TEXT");
            b.Property(x => x.BalanceMinor).HasColumnType("INTEGER");
            b.HasData(new Account(1, "Checking"), new Account(2, "Savings"));
        });
        modelBuilder.Entity<BankTransaction>(b =>
        {
            b.ToTable(
                "Transactions",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_Transactions_Amount",
                        "typeof(AmountMinor) = 'integer' AND AmountMinor BETWEEN 1 AND 1000000000"
                    );
                    table.HasCheckConstraint(
                        "CK_Transactions_SourceBalance",
                        "SourceBalanceAfterMinor IS NULL OR (typeof(SourceBalanceAfterMinor) = 'integer' AND SourceBalanceAfterMinor BETWEEN 0 AND 1000000000)"
                    );
                    table.HasCheckConstraint(
                        "CK_Transactions_DestinationBalance",
                        "DestinationBalanceAfterMinor IS NULL OR (typeof(DestinationBalanceAfterMinor) = 'integer' AND DestinationBalanceAfterMinor BETWEEN 0 AND 1000000000)"
                    );
                    table.HasCheckConstraint(
                        "CK_Transactions_Shape",
                        "(Type = 'Deposit' AND SourceAccountId IS NULL AND SourceBalanceAfterMinor IS NULL AND DestinationAccountId IS NOT NULL AND DestinationBalanceAfterMinor IS NOT NULL) OR (Type = 'Withdrawal' AND SourceAccountId IS NOT NULL AND SourceBalanceAfterMinor IS NOT NULL AND DestinationAccountId IS NULL AND DestinationBalanceAfterMinor IS NULL) OR (Type = 'Transfer' AND SourceAccountId IS NOT NULL AND DestinationAccountId IS NOT NULL AND SourceAccountId <> DestinationAccountId AND SourceBalanceAfterMinor IS NOT NULL AND DestinationBalanceAfterMinor IS NOT NULL)"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever().HasColumnType("TEXT");
            b.Property(x => x.IdempotencyKey).HasColumnType("TEXT");
            b.Property(x => x.Type).HasConversion<string>().IsRequired().HasColumnType("TEXT");
            b.Property(x => x.SourceAccountId).HasColumnType("INTEGER");
            b.Property(x => x.DestinationAccountId).HasColumnType("INTEGER");
            b.Property(x => x.AmountMinor).HasColumnType("INTEGER");
            b.Property(x => x.SourceBalanceAfterMinor).HasColumnType("INTEGER");
            b.Property(x => x.DestinationBalanceAfterMinor).HasColumnType("INTEGER");
            b.Property(x => x.CreatedAtUtc)
                .HasColumnType("TEXT")
                .HasConversion(
                    new ValueConverter<DateTime, DateTime>(
                        value => value,
                        value => DateTime.SpecifyKind(value, DateTimeKind.Utc)
                    )
                );
            b.HasOne<Account>()
                .WithMany()
                .HasForeignKey(x => x.SourceAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Account>()
                .WithMany()
                .HasForeignKey(x => x.DestinationAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.IdempotencyKey).IsUnique();
            b.HasIndex(x => new
            {
                x.SourceAccountId,
                x.CreatedAtUtc,
                x.Id,
            });
            b.HasIndex(x => new
            {
                x.DestinationAccountId,
                x.CreatedAtUtc,
                x.Id,
            });
        });
    }
}
