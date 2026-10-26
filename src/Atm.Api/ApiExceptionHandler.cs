using Atm.Application;
using Microsoft.AspNetCore.Diagnostics;

namespace Atm.Api;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        logger.LogError(exception, "Request failed; trace {TraceId}", context.TraceIdentifier);
        IResult result;
        if (exception is DatabaseBusyException)
        {
            context.Response.Headers.RetryAfter = "1";
            result = ApiProblems.Create(
                context,
                503,
                "database_busy",
                "The database is busy. Retry this request with the same Idempotency-Key."
            );
        }
        else if (exception is BadHttpRequestException badRequest)
        {
            result =
                badRequest.StatusCode == 400
                    ? ApiProblems.Validation(context, "body", "The request body is invalid.")
                    : ApiProblems.Create(
                        context,
                        badRequest.StatusCode,
                        "invalid_request",
                        "The HTTP request could not be read."
                    );
        }
        else
        {
            result = ApiProblems.Create(
                context,
                500,
                "internal_error",
                "An unexpected error occurred. Retry a transaction with the same Idempotency-Key."
            );
        }
        await result.ExecuteAsync(context);
        return true;
    }
}
