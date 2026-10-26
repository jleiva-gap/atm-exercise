using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atm.Application;
using Microsoft.Net.Http.Headers;

namespace Atm.Api;

internal static class ApiEndpoints
{
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    public static void MapAtmApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet(
            "/accounts",
            async (IAtmStore store, CancellationToken ct) =>
                Results.Ok(await store.GetAccountsAsync(ct))
        );
        api.MapGet("/accounts/{id}/transactions", GetHistory);
        api.MapGet("/transactions/{id}", GetReceipt);
        api.MapPost("/transactions", ExecuteTransaction);
    }

    private static async Task<IResult> GetReceipt(
        string id,
        IAtmStore store,
        HttpContext context,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(id, out var parsed))
            return ApiProblems.Validation(context, "id", "Transaction ID must be a UUID.");
        var receipt = await store.GetReceiptAsync(parsed, ct);
        return receipt is null
            ? ApiProblems.Create(
                context,
                404,
                "transaction_not_found",
                "The transaction does not exist."
            )
            : Results.Ok(receipt);
    }

    private static async Task<IResult> GetHistory(
        string id,
        IAtmStore store,
        HttpContext context,
        CancellationToken ct
    )
    {
        if (
            !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId)
            || accountId < 1
        )
            return ApiProblems.Validation(context, "id", "Account ID must be a positive integer.");
        var errors = new Dictionary<string, string[]>();
        var page = ReadPageParameter(context.Request, "page", 1, int.MaxValue, errors);
        var pageSize = ReadPageParameter(context.Request, "pageSize", 20, 100, errors);
        if (errors.Count > 0)
            return ApiProblems.Validation(context, errors);
        var history = await store.GetHistoryAsync(accountId, page, pageSize, ct);
        return history is null
            ? ApiProblems.Create(
                context,
                404,
                "account_not_found",
                $"Account {accountId} does not exist."
            )
            : Results.Ok(history);
    }

    private static int ReadPageParameter(
        HttpRequest request,
        string name,
        int defaultValue,
        int maximum,
        Dictionary<string, string[]> errors
    )
    {
        if (!request.Query.TryGetValue(name, out var values))
            return defaultValue;
        if (
            values.Count == 1
            && int.TryParse(
                values[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value
            )
            && value >= 1
            && value <= maximum
        )
            return value;
        errors[name] = [$"{name} must be one integer between 1 and {maximum}."];
        return defaultValue;
    }

    private static async Task<IResult> ExecuteTransaction(
        HttpContext context,
        TransactionService service,
        CancellationToken ct
    )
    {
        if (!context.Request.HasJsonContentType())
            return ApiProblems.Create(
                context,
                415,
                "unsupported_media_type",
                "Use Content-Type: application/json."
            );
        if (
            MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
            && contentType.Charset.HasValue
        )
        {
            try
            {
                _ = Encoding.GetEncoding(HeaderUtilities.RemoveQuotes(contentType.Charset).Value!);
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException)
            {
                return ApiProblems.Create(
                    context,
                    415,
                    "unsupported_media_type",
                    "The JSON charset is not supported. Use UTF-8."
                );
            }
        }
        if (
            !context.Request.Headers.TryGetValue("Idempotency-Key", out var keys)
            || keys.Count != 1
            || !Guid.TryParse(keys[0], out var key)
        )
            return ApiProblems.Validation(
                context,
                "Idempotency-Key",
                "Exactly one UUID Idempotency-Key header is required."
            );

        TransactionRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<TransactionRequest>(RequestJson, ct);
        }
        catch (JsonException)
        {
            return ApiProblems.Validation(
                context,
                "body",
                "Provide a JSON object with known camelCase properties and integer numeric fields."
            );
        }
        if (request is null)
            return ApiProblems.Validation(context, "body", "A transaction object is required.");
        var command = request.ToCommand();
        var errors = command.Validate();
        if (errors.Count != 0)
            return ApiProblems.Validation(context, errors);
        var result = await service.ExecuteAsync(command, key, ct);
        return result.Error is { } failure
            ? ApiProblems.BusinessFailure(context, failure)
            : Results.Created($"/api/transactions/{result.Value!.Id}", result.Value);
    }
}
