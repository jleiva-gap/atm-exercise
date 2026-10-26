using System.Diagnostics;
using System.Net;
using Atm.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Atm.Api.Tests.ApiClient;
using static Atm.Api.Tests.ValidationTests;

namespace Atm.Api.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task Competing_withdrawals_across_hosts_cannot_overdraw()
    {
        using var first = new AtmFactory();
        using var client1 = first.CreateClient();
        using var second = new AtmFactory(first.DatabasePath);
        using var client2 = second.CreateClient();
        await Deposit(client1, 100);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable
            .Range(0, 10)
            .Select(index =>
                Task.Run(async () =>
                {
                    await gate.Task;
                    return await Post(
                        index % 2 == 0 ? client1 : client2,
                        """{"type":"withdrawal","sourceAccountId":1,"amountMinor":20}"""
                    );
                })
            )
            .ToArray();
        gate.SetResult();
        var responses = await Task.WhenAll(tasks);
        Assert.Equal(5, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(new long[] { 0, 0 }, await Balances(client1));
        var history = await Json(await client1.GetAsync("/api/accounts/1/transactions"));
        Assert.Equal(6, history.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Concurrent_identical_keys_across_hosts_create_one_receipt()
    {
        using var first = new AtmFactory();
        using var client1 = first.CreateClient();
        using var second = new AtmFactory(first.DatabasePath);
        using var client2 = second.CreateClient();
        var key = Guid.NewGuid().ToString();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable
            .Range(0, 8)
            .Select(index =>
                Task.Run(async () =>
                {
                    await gate.Task;
                    return await Deposit(index % 2 == 0 ? client1 : client2, 101, key: key);
                })
            )
            .ToArray();
        gate.SetResult();
        var responses = await Task.WhenAll(tasks);
        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.Created, response.StatusCode)
        );
        var bodies = await Task.WhenAll(responses.Select(x => x.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
        Assert.Single(responses.Select(x => x.Headers.Location).Distinct());
        Assert.Equal(new long[] { 101, 0 }, await Balances(client1));
        Assert.Single(
            (await Json(await client1.GetAsync("/api/accounts/1/transactions")))
                .GetProperty("items")
                .EnumerateArray()
        );
    }

    [Fact]
    public async Task Concurrent_changed_payloads_with_one_key_accept_only_one()
    {
        using var first = new AtmFactory();
        using var client1 = first.CreateClient();
        using var second = new AtmFactory(first.DatabasePath);
        using var client2 = second.CreateClient();
        var key = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(
            Task.Run(() => Deposit(client1, 100, key: key)),
            Task.Run(() => Deposit(client2, 200, key: key))
        );
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var receipt = await Json(responses.Single(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(receipt.GetProperty("amountMinor").GetInt64(), (await Balances(client1))[0]);
    }

    [Fact]
    public async Task Restart_replays_original_receipt_and_lost_response_without_mutating_again()
    {
        using var owner = new AtmFactory();
        var key = Guid.NewGuid().ToString();
        var lostKey = Guid.NewGuid().ToString();
        string original;
        Uri? location;
        using (var first = new AtmFactory(owner.DatabasePath))
        using (var client = first.CreateClient())
        {
            var result = await Deposit(client, 101, key: key);
            Assert.Equal(HttpStatusCode.Created, result.StatusCode);
            original = await result.Content.ReadAsStringAsync();
            location = result.Headers.Location;
            // Discard an acknowledged server response to model a client that never received it.
            using var ignored = await Deposit(client, 202, key: lostKey);
            Assert.Equal(HttpStatusCode.Created, ignored.StatusCode);
            await Deposit(client, 303);
        }
        using var restarted = new AtmFactory(owner.DatabasePath);
        using var restartedClient = restarted.CreateClient();
        var replay = await Deposit(restartedClient, 101, key: key);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        Assert.Equal(location, replay.Headers.Location);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(
            original,
            await (await restartedClient.GetAsync(location)).Content.ReadAsStringAsync()
        );
        var lostReplay = await Deposit(restartedClient, 202, key: lostKey);
        Assert.Equal(
            303,
            (await Json(lostReplay)).GetProperty("destinationBalanceAfterMinor").GetInt64()
        );
        Assert.Equal(new long[] { 606, 0 }, await Balances(restartedClient));
        Assert.Equal(
            3,
            (await Json(await restartedClient.GetAsync("/api/accounts/1/transactions")))
                .GetProperty("items")
                .GetArrayLength()
        );
    }

    [Fact]
    public async Task Lock_timeout_is_503_and_same_key_can_succeed_after_release()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await using var connection = new SqliteConnection(
            $"Data Source={app.DatabasePath};Pooling=False"
        );
        await connection.OpenAsync();
        var key = Guid.NewGuid().ToString();
        await using (var transaction = connection.BeginTransaction(deferred: false))
        {
            var timer = Stopwatch.StartNew();
            var response = await Deposit(client, 100, key: key);
            await AssertProblem(response, 503, "database_busy");
            Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
            Assert.Contains(
                "same Idempotency-Key",
                (await Json(response)).GetProperty("detail").GetString()
            );
            Assert.InRange(timer.Elapsed.TotalSeconds, 4.5, 15);
        }
        Assert.Equal(new long[] { 0, 0 }, await Balances(client));
        Assert.Equal(HttpStatusCode.Created, (await Deposit(client, 100, key: key)).StatusCode);
    }

    [Fact]
    public async Task Receipt_insert_failure_rolls_back_balances_and_is_not_a_duplicate_conflict()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await Deposit(client, 1000);
        // A real SQLite constraint failure after SaveChanges has started. No production failure hooks.
        await app.ExecuteSql(
            """
            CREATE TRIGGER FailReceipt BEFORE INSERT ON Transactions
            BEGIN SELECT RAISE(ABORT, 'private SQL /secret/database/path'); END;
            """
        );
        var key = Guid.NewGuid().ToString();
        const string body =
            """{"type":"transfer","sourceAccountId":1,"destinationAccountId":2,"amountMinor":100}""";
        var response = await Post(client, body, key);
        await AssertProblem(response, 500, "internal_error");
        var error = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private", error);
        Assert.DoesNotContain("SQLite", error);
        Assert.DoesNotContain("secret", error);
        Assert.DoesNotContain(app.DatabasePath, error);
        Assert.Equal(new long[] { 1000, 0 }, await Balances(client));
        Assert.Single(
            (await Json(await client.GetAsync("/api/accounts/1/transactions")))
                .GetProperty("items")
                .EnumerateArray()
        );
        Assert.Empty(
            (await Json(await client.GetAsync("/api/accounts/2/transactions")))
                .GetProperty("items")
                .EnumerateArray()
        );
        await app.ExecuteSql("DROP TRIGGER FailReceipt;");
        Assert.Equal(HttpStatusCode.Created, (await Post(client, body, key)).StatusCode);
        Assert.Equal(new long[] { 900, 100 }, await Balances(client));
    }

    [Fact]
    public async Task Failure_after_first_account_update_rolls_back_the_entire_transaction()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await Deposit(client, 1000);
        // An AFTER trigger runs only after a write, then deliberately writes invalid state in the other account.
        await app.ExecuteSql(
            """
            CREATE TRIGGER FailAfterDebit AFTER UPDATE OF BalanceMinor ON Accounts
            WHEN NEW.Id = 1 AND NEW.BalanceMinor < OLD.BalanceMinor
            BEGIN UPDATE Accounts SET BalanceMinor = -1 WHERE Id = 2; END;
            """
        );
        var response = await Post(
            client,
            """{"type":"transfer","sourceAccountId":1,"destinationAccountId":2,"amountMinor":100}"""
        );
        await AssertProblem(response, 500, "internal_error");
        Assert.Equal(new long[] { 1000, 0 }, await Balances(client));
        Assert.Single(
            (await Json(await client.GetAsync("/api/accounts/1/transactions")))
                .GetProperty("items")
                .EnumerateArray()
        );
        Assert.Empty(
            (await Json(await client.GetAsync("/api/accounts/2/transactions")))
                .GetProperty("items")
                .EnumerateArray()
        );
    }

    [Fact]
    public async Task Unrelated_unique_constraint_failure_is_500_not_idempotency_conflict()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await app.ExecuteSql("CREATE UNIQUE INDEX TestUniqueAmount ON Transactions(AmountMinor);");
        await Deposit(client, 100);
        var response = await Deposit(client, 100);
        await AssertProblem(response, 500, "internal_error");
        Assert.Equal(new long[] { 100, 0 }, await Balances(client));
    }

    [Theory]
    [InlineData("UPDATE Accounts SET BalanceMinor = -1 WHERE Id = 1;")]
    [InlineData("UPDATE Accounts SET BalanceMinor = 1000000001 WHERE Id = 1;")]
    [InlineData("UPDATE Accounts SET BalanceMinor = 1.5 WHERE Id = 1;")]
    [InlineData("INSERT INTO Accounts (Id, Name, BalanceMinor) VALUES (3, 'Other', 0);")]
    [InlineData("UPDATE Transactions SET AmountMinor = 0;")]
    [InlineData("UPDATE Transactions SET AmountMinor = 1.5;")]
    [InlineData("UPDATE Transactions SET AmountMinor = 1000000001;")]
    [InlineData("UPDATE Transactions SET DestinationAccountId = 3;")]
    [InlineData("UPDATE Transactions SET SourceAccountId = 1;")]
    [InlineData("UPDATE Transactions SET DestinationBalanceAfterMinor = NULL;")]
    [InlineData("UPDATE Transactions SET Type = 'Other';")]
    public async Task Database_constraints_reject_invalid_state(string sql)
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await Deposit(client, 100);
        var exception = await Assert.ThrowsAsync<SqliteException>(() => app.ExecuteSql(sql));
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(new long[] { 100, 0 }, await Balances(client));
    }

    [Fact]
    public async Task Migration_model_matches_runtime_and_every_connection_enforces_foreign_keys()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AtmDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Equal_timestamps_use_id_as_stable_history_tiebreaker()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await Deposit(client, 100);
        await Deposit(client, 200);
        await app.ExecuteSql("UPDATE Transactions SET CreatedAtUtc = '2026-10-23 00:00:00';");
        var full = (await Json(await client.GetAsync("/api/accounts/1/transactions"))).GetProperty(
            "items"
        );
        var ids = full.EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(ids.OrderByDescending(x => x, StringComparer.Ordinal), ids);
        var page1 = (
            await Json(await client.GetAsync("/api/accounts/1/transactions?pageSize=1"))
        ).GetProperty("items")[0];
        var page2 = (
            await Json(await client.GetAsync("/api/accounts/1/transactions?page=2&pageSize=1"))
        ).GetProperty("items")[0];
        Assert.Equal(ids[0], page1.GetProperty("id").GetString());
        Assert.Equal(ids[1], page2.GetProperty("id").GetString());
    }
}
