using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atm.Infrastructure.Migrations;

[DbContext(typeof(AtmDbContext))]
[Migration("202610230001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        InitialModel.Build(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE Accounts (
                Id INTEGER NOT NULL CONSTRAINT PK_Accounts PRIMARY KEY,
                Name TEXT NOT NULL,
                BalanceMinor INTEGER NOT NULL,
                CONSTRAINT CK_Accounts_Id CHECK (Id IN (1, 2)),
                CONSTRAINT CK_Accounts_Balance CHECK (typeof(BalanceMinor) = 'integer' AND BalanceMinor BETWEEN 0 AND 1000000000)
            );
            INSERT INTO Accounts (Id, Name, BalanceMinor) VALUES (1, 'Checking', 0), (2, 'Savings', 0);
            CREATE TABLE Transactions (
                Id TEXT NOT NULL CONSTRAINT PK_Transactions PRIMARY KEY,
                IdempotencyKey TEXT NOT NULL,
                Type TEXT NOT NULL,
                SourceAccountId INTEGER NULL,
                DestinationAccountId INTEGER NULL,
                AmountMinor INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                SourceBalanceAfterMinor INTEGER NULL,
                DestinationBalanceAfterMinor INTEGER NULL,
                CONSTRAINT FK_Transactions_Accounts_SourceAccountId FOREIGN KEY (SourceAccountId) REFERENCES Accounts (Id) ON DELETE RESTRICT,
                CONSTRAINT FK_Transactions_Accounts_DestinationAccountId FOREIGN KEY (DestinationAccountId) REFERENCES Accounts (Id) ON DELETE RESTRICT,
                CONSTRAINT CK_Transactions_Amount CHECK (typeof(AmountMinor) = 'integer' AND AmountMinor BETWEEN 1 AND 1000000000),
                CONSTRAINT CK_Transactions_SourceBalance CHECK (SourceBalanceAfterMinor IS NULL OR (typeof(SourceBalanceAfterMinor) = 'integer' AND SourceBalanceAfterMinor BETWEEN 0 AND 1000000000)),
                CONSTRAINT CK_Transactions_DestinationBalance CHECK (DestinationBalanceAfterMinor IS NULL OR (typeof(DestinationBalanceAfterMinor) = 'integer' AND DestinationBalanceAfterMinor BETWEEN 0 AND 1000000000)),
                CONSTRAINT CK_Transactions_Shape CHECK (
                    (Type = 'Deposit' AND SourceAccountId IS NULL AND SourceBalanceAfterMinor IS NULL AND DestinationAccountId IS NOT NULL AND DestinationBalanceAfterMinor IS NOT NULL) OR
                    (Type = 'Withdrawal' AND SourceAccountId IS NOT NULL AND SourceBalanceAfterMinor IS NOT NULL AND DestinationAccountId IS NULL AND DestinationBalanceAfterMinor IS NULL) OR
                    (Type = 'Transfer' AND SourceAccountId IS NOT NULL AND DestinationAccountId IS NOT NULL AND SourceAccountId <> DestinationAccountId AND SourceBalanceAfterMinor IS NOT NULL AND DestinationBalanceAfterMinor IS NOT NULL)
                )
            );
            CREATE UNIQUE INDEX IX_Transactions_IdempotencyKey ON Transactions (IdempotencyKey);
            CREATE INDEX IX_Transactions_SourceAccountId_CreatedAtUtc_Id ON Transactions (SourceAccountId, CreatedAtUtc, Id);
            CREATE INDEX IX_Transactions_DestinationAccountId_CreatedAtUtc_Id ON Transactions (DestinationAccountId, CreatedAtUtc, Id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Transactions");
        migrationBuilder.DropTable("Accounts");
    }
}
