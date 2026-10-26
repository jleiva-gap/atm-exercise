using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atm.Api.Tests;

public sealed class AtmFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; }
    private readonly bool ownsFile;

    public AtmFactory(string? path = null)
    {
        ownsFile = path is null;
        DatabasePath = path ?? Path.Combine(Path.GetTempPath(), $"atm-tests-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Atm:DatabasePath"] = DatabasePath }
                )
        );
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    public async Task ExecuteSql(string sql)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                ForeignKeys = true,
                Pooling = false,
            }.ToString()
        );
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && ownsFile)
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                File.Delete(DatabasePath + suffix);
        }
    }
}

internal static class ApiClient
{
    public static Task<HttpResponseMessage> Post(HttpClient client, string json, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> Deposit(
        HttpClient client,
        long amount,
        int account = 1,
        string? key = null
    ) =>
        Post(
            client,
            JsonSerializer.Serialize(
                new
                {
                    type = "deposit",
                    destinationAccountId = account,
                    amountMinor = amount,
                }
            ),
            key
        );

    public static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    public static async Task<long[]> Balances(HttpClient client) =>
        (await Json(await client.GetAsync("/api/accounts")))
            .EnumerateArray()
            .Select(x => x.GetProperty("balanceMinor").GetInt64())
            .ToArray();
}
