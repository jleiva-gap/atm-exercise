using Atm.Domain;

namespace Atm.Domain.Tests;

public class AccountTests
{
    [Fact]
    public void Deposit_and_withdraw_keep_exact_cents()
    {
        var account = new Account(1, "Checking");
        Assert.Null(account.Deposit(1250));
        Assert.Null(account.Withdraw(249));
        Assert.Equal(1001, account.BalanceMinor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    [InlineData(long.MaxValue)]
    public void Invalid_amount_does_not_change_balance(long amount)
    {
        var account = new Account(1, "Checking");
        Assert.Equal("invalid_amount", account.Deposit(amount)?.Code);
        Assert.Equal("invalid_amount", account.Withdraw(amount)?.Code);
        Assert.Equal(0, account.BalanceMinor);
    }

    [Fact]
    public void Balance_can_reach_limit_and_zero_but_cannot_exceed_either()
    {
        var account = new Account(1, "Checking");
        Assert.Null(account.Deposit(1_000_000_000));
        Assert.Equal("balance_limit_exceeded", account.Deposit(1)?.Code);
        Assert.Equal(1_000_000_000, account.BalanceMinor);
        Assert.Null(account.Withdraw(1_000_000_000));
        Assert.Equal("insufficient_funds", account.Withdraw(1)?.Code);
        Assert.Equal(0, account.BalanceMinor);
    }

    [Fact]
    public void Transfer_conserves_total_funds()
    {
        var source = new Account(1, "Checking");
        var destination = new Account(2, "Savings");
        source.Deposit(2000);
        Assert.Null(Account.Transfer(source, destination, 749));
        Assert.Equal(1251, source.BalanceMinor);
        Assert.Equal(749, destination.BalanceMinor);
    }

    [Fact]
    public void Rejected_transfer_does_not_partially_mutate_accounts()
    {
        var source = new Account(1, "Checking");
        var destination = new Account(2, "Savings");
        source.Deposit(100);
        destination.Deposit(1_000_000_000);
        Assert.Equal("balance_limit_exceeded", Account.Transfer(source, destination, 1)?.Code);
        Assert.Equal("insufficient_funds", Account.Transfer(source, destination, 101)?.Code);
        Assert.Equal("invalid_accounts", Account.Transfer(source, source, 1)?.Code);
        Assert.Equal("invalid_amount", Account.Transfer(source, destination, 0)?.Code);
        Assert.Equal(100, source.BalanceMinor);
        Assert.Equal(1_000_000_000, destination.BalanceMinor);
    }
}
