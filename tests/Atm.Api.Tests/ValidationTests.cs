using System.Net;
using System.Text;
using static Atm.Api.Tests.ApiClient;

namespace Atm.Api.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"type\":\"Deposit\",\"destinationAccountId\":1,\"amountMinor\":1}")]
    [InlineData("{\"type\":\"unknown\",\"destinationAccountId\":1,\"amountMinor\":1}")]
    [InlineData("{\"type\":0,\"destinationAccountId\":1,\"amountMinor\":1}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":0}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":-1}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":1000000001}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":1.25}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":\"100\"}")]
    [InlineData(
        "{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":9223372036854775808}"
    )]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":null}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":1}")]
    [InlineData("{\"type\":\"deposit\",\"amountMinor\":1}")]
    [InlineData(
        "{\"type\":\"deposit\",\"sourceAccountId\":1,\"destinationAccountId\":2,\"amountMinor\":1}"
    )]
    [InlineData("{\"type\":\"withdrawal\",\"destinationAccountId\":1,\"amountMinor\":1}")]
    [InlineData(
        "{\"type\":\"transfer\",\"sourceAccountId\":1,\"destinationAccountId\":1,\"amountMinor\":1}"
    )]
    [InlineData("{\"type\":\"transfer\",\"sourceAccountId\":1,\"amountMinor\":1}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":0,\"amountMinor\":1}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":-1,\"amountMinor\":1}")]
    [InlineData("{\"type\":\"deposit\",\"destinationAccountId\":2147483648,\"amountMinor\":1}")]
    [InlineData(
        "{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":1,\"extra\":true}"
    )]
    [InlineData(
        "{\"type\":\"deposit\",\"destinationAccountId\":1,\"amountMinor\":1,\"amountMinor\":2}"
    )]
    public async Task Invalid_json_and_commands_are_field_keyed_400_without_mutation(string body)
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var response = await Post(client, body);
        await AssertProblem(response, 400, "validation_error", true);
        Assert.Equal(new long[] { 0, 0 }, await Balances(client));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("d3e35b75-e159-441d-a920-527b7ef5a70e,09b3b75a-4c00-43ec-9aa5-8148bb0d4f0b")]
    public async Task Missing_or_invalid_key_is_400(string? key)
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions")
        {
            Content = new StringContent(
                """{"type":"deposit","destinationAccountId":1,"amountMinor":1}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
        if (key is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        await AssertProblem(await client.SendAsync(request), 400, "validation_error", true);
    }

    [Fact]
    public async Task Multiple_header_values_are_400()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }
        );
        await AssertProblem(await client.SendAsync(request), 400, "validation_error", true);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Non_json_body_is_415(string contentType)
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        var response = await client.PostAsync(
            "/api/transactions",
            new StringContent("{}", Encoding.UTF8, contentType)
        );
        await AssertProblem(response, 415, "unsupported_media_type");
    }

    [Theory]
    [InlineData("/api/accounts/no/transactions")]
    [InlineData("/api/accounts/0/transactions")]
    [InlineData("/api/accounts/2147483648/transactions")]
    [InlineData("/api/transactions/not-a-uuid")]
    [InlineData("/api/accounts/1/transactions?page=0")]
    [InlineData("/api/accounts/1/transactions?page=-1")]
    [InlineData("/api/accounts/1/transactions?page=2147483648")]
    [InlineData("/api/accounts/1/transactions?page=1&page=2")]
    [InlineData("/api/accounts/1/transactions?page=")]
    [InlineData("/api/accounts/1/transactions?pageSize=0")]
    [InlineData("/api/accounts/1/transactions?pageSize=101")]
    [InlineData("/api/accounts/1/transactions?pageSize=abc")]
    public async Task Invalid_ids_and_pagination_are_400(string url)
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await AssertProblem(await client.GetAsync(url), 400, "validation_error", true);
    }

    [Fact]
    public async Task Large_valid_page_does_not_overflow()
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await Deposit(client, 1);
        var response = await client.GetAsync(
            "/api/accounts/1/transactions?page=2147483647&pageSize=100"
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response)).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("/api/accounts/3/transactions", "account_not_found")]
    [InlineData("/api/transactions/fc72aaf1-a470-429a-906b-5e023bbad209", "transaction_not_found")]
    public async Task Well_formed_missing_resources_are_404(string url, string code)
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        await AssertProblem(await client.GetAsync(url), 404, code);
    }

    [Theory]
    [InlineData("application/json; charset=not-a-real-encoding")]
    [InlineData("application/json; charset=\"not-a-real-encoding\"")]
    public async Task Unsupported_charset_is_415_without_mutation(string contentType)
    {
        using var app = new AtmFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions")
        {
            Content = new StringContent(
                """{"type":"deposit","destinationAccountId":1,"amountMinor":100}"""
            ),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content.Headers.Remove("Content-Type");
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        await AssertProblem(await client.SendAsync(request), 415, "unsupported_media_type");
        Assert.Equal(new long[] { 0, 0 }, await Balances(client));
    }

    internal static async Task AssertProblem(
        HttpResponseMessage response,
        int status,
        string code,
        bool validation = false
    )
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await Json(response);
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        foreach (var field in new[] { "type", "title", "detail", "traceId" })
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty(field).GetString()));
        if (validation)
            Assert.NotEmpty(problem.GetProperty("errors").EnumerateObject());
    }
}
