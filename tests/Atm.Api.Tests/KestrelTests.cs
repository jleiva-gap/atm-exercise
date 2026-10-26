using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Atm.Api.Tests.ApiClient;
using static Atm.Api.Tests.ValidationTests;

namespace Atm.Api.Tests;

public class KestrelTests
{
    [Fact]
    public async Task Real_server_logs_database_failure_and_returns_safe_json_without_platform_log_permissions()
    {
        using var database = new AtmFactory();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.ArgumentList.Add("--Atm:DatabasePath");
        start.ArgumentList.Add(database.DatabasePath);
        using var server = Process.Start(start)!;
        var output = server.StandardOutput.ReadToEndAsync();
        var error = server.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };
            var ready = false;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(20) && !server.HasExited)
            {
                try
                {
                    using var response = await client.GetAsync("/api/accounts");
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        ready = true;
                        break;
                    }
                }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            Assert.True(ready, "Real API process did not become ready.");
            await database.ExecuteSql(
                """
                CREATE TRIGGER FailReceipt BEFORE INSERT ON Transactions
                BEGIN SELECT RAISE(ABORT, 'private persistence detail'); END;
                """
            );
            var key = Guid.NewGuid().ToString();
            var response500 = await Deposit(client, 100, key: key);
            await AssertProblem(response500, 500, "internal_error");
            Assert.DoesNotContain(
                "private persistence detail",
                await response500.Content.ReadAsStringAsync()
            );
            Assert.Equal(new long[] { 0, 0 }, await Balances(client));
            await database.ExecuteSql("DROP TRIGGER FailReceipt;");
            Assert.Equal(HttpStatusCode.Created, (await Deposit(client, 100, key: key)).StatusCode);
        }
        finally
        {
            if (!server.HasExited)
                server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
        Assert.Contains("private persistence detail", await output);
        Assert.DoesNotContain("Cannot open log", (await output) + (await error));
    }
}
