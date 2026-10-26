using System.Diagnostics;
using Atm.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Atm.Api;

internal static class ApiProblems
{
    public static IResult Create(
        HttpContext context,
        int status,
        string code,
        string detail,
        IDictionary<string, string[]>? errors = null
    )
    {
        ProblemDetails problem = errors is null
            ? new ProblemDetails()
            : new HttpValidationProblemDetails(errors);
        problem.Type = $"https://httpstatuses.com/{status}";
        problem.Title = ReasonPhrases.GetReasonPhrase(status);
        problem.Status = status;
        problem.Detail = detail;
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        if (status < 500)
            context
                .RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Atm.Api.Validation")
                .LogInformation(
                    "Request rejected: {Code}; trace {TraceId}",
                    code,
                    context.TraceIdentifier
                );
        return Results.Problem(problem);
    }

    public static IResult Validation(HttpContext context, IDictionary<string, string[]> errors) =>
        Create(context, 400, "validation_error", "One or more request fields are invalid.", errors);

    public static IResult Validation(HttpContext context, string field, string message) =>
        Validation(context, new Dictionary<string, string[]> { [field] = [message] });

    public static IResult BusinessFailure(HttpContext context, Failure failure) =>
        Create(
            context,
            failure.Code switch
            {
                "account_not_found" => 404,
                "insufficient_funds" or "balance_limit_exceeded" or "idempotency_conflict" => 409,
                _ => 400,
            },
            failure.Code,
            failure.Detail
        );
}
