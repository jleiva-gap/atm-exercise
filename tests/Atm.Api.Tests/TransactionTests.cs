using System.Net;
using static Atm.Api.Tests.ApiClient;

namespace Atm.Api.Tests;

public class TransactionTests
{
    [Fact]
    public async Task Accounts_start_at_zero_in_id_order_and_empty_history_is_paginated()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var response = await client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await Json(response);
        Assert.Equal(2, accounts.GetArrayLength());
        Assert.Equal(1, accounts[0].GetProperty("id").GetInt32());
        Assert.Equal("Checking", accounts[0].GetProperty("name").GetString());
        Assert.Equal("Savings", accounts[1].GetProperty("name").GetString());
        Assert.Equal("USD", accounts[0].GetProperty("currency").GetString());
        Assert.Equal(new long[] { 0, 0 }, await Balances(client));
        var history = await Json(await client.GetAsync("/api/accounts/1/transactions"));
        Assert.Equal(0, history.GetProperty("items").GetArrayLength());
        Assert.Equal(1, history.GetProperty("page").GetInt32());
        Assert.Equal(20, history.GetProperty("pageSize").GetInt32());
        Assert.False(history.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Deposit_withdraw_transfer_return_receipts_and_shared_history()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await Deposit(client, 1250)).StatusCode);
        var withdraw = await Post(
            client,
            """{"type":"withdrawal","sourceAccountId":1,"amountMinor":249}"""
        );
        Assert.Equal(HttpStatusCode.Created, withdraw.StatusCode);
        var transfer = await Post(
            client,
            """{"type":"transfer","sourceAccountId":1,"destinationAccountId":2,"amountMinor":501}"""
        );
        Assert.Equal(HttpStatusCode.Created, transfer.StatusCode);
        var receipt = await Json(transfer);
        Assert.Equal(500, receipt.GetProperty("sourceBalanceAfterMinor").GetInt64());
        Assert.Equal(501, receipt.GetProperty("destinationBalanceAfterMinor").GetInt64());
        Assert.Equal("USD", receipt.GetProperty("currency").GetString());
        Assert.EndsWith("Z", receipt.GetProperty("createdAtUtc").GetString());
        Assert.False(receipt.TryGetProperty("idempotencyKey", out _));
        Assert.Equal(
            $"/api/transactions/{receipt.GetProperty("id").GetString()}",
            transfer.Headers.Location?.ToString()
        );
        var get = await client.GetAsync(transfer.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(receipt.GetRawText(), (await Json(get)).GetRawText());
        Assert.Equal(new long[] { 500, 501 }, await Balances(client));
        var history1 = await Json(await client.GetAsync("/api/accounts/1/transactions?pageSize=2"));
        Assert.True(history1.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, history1.GetProperty("items").GetArrayLength());
        var history2 = await Json(await client.GetAsync("/api/accounts/2/transactions"));
        Assert.Equal(
            receipt.GetProperty("id").GetString(),
            history2.GetProperty("items")[0].GetProperty("id").GetString()
        );
        var page2 = await Json(
            await client.GetAsync("/api/accounts/1/transactions?page=2&pageSize=2")
        );
        Assert.Single(page2.GetProperty("items").EnumerateArray());
        Assert.False(page2.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Replay_returns_original_receipt_after_other_operations_and_rejects_changed_payload()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var key = Guid.NewGuid();
        var first = await Deposit(client, 100, key: key.ToString("D"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await Deposit(client, 200);
        var replay = await Post(
            client,
            """{"type":"deposit","sourceAccountId":null,"destinationAccountId":1,"amountMinor":100}""",
            key.ToString("N").ToUpperInvariant()
        );
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await replay.Content.ReadAsStringAsync()
        );
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        var conflict = await Deposit(client, 101, key: key.ToString());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(
            "idempotency_conflict",
            (await Json(conflict)).GetProperty("code").GetString()
        );
        Assert.Equal(new long[] { 300, 0 }, await Balances(client));
    }

    [Theory]
    [InlineData(
        "{\"type\":\"withdrawal\",\"sourceAccountId\":1,\"amountMinor\":1}",
        409,
        "insufficient_funds"
    )]
    [InlineData(
        "{\"type\":\"deposit\",\"destinationAccountId\":3,\"amountMinor\":1}",
        404,
        "account_not_found"
    )]
    [InlineData(
        "{\"type\":\"transfer\",\"sourceAccountId\":1,\"destinationAccountId\":3,\"amountMinor\":1}",
        404,
        "account_not_found"
    )]
    public async Task Business_failures_do_not_reserve_key_or_write_history(
        string body,
        int status,
        string code
    )
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var key = Guid.NewGuid().ToString();
        var response = await Post(client, body, key);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
        Assert.Equal(new long[] { 0, 0 }, await Balances(client));
        var history = await Json(await client.GetAsync("/api/accounts/1/transactions"));
        Assert.Empty(history.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Created, (await Deposit(client, 1, key: key)).StatusCode);
    }

    [Fact]
    public async Task Transfer_to_full_account_preserves_both_balances()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await Deposit(client, 100);
        await Deposit(client, 1_000_000_000, 2);
        var response = await Post(
            client,
            """{"type":"transfer","sourceAccountId":1,"destinationAccountId":2,"amountMinor":1}"""
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "balance_limit_exceeded",
            (await Json(response)).GetProperty("code").GetString()
        );
        Assert.Equal(new long[] { 100, 1_000_000_000 }, await Balances(client));
    }
}
