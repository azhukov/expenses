using Expenses.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace Expenses.Api;

/// <summary>
/// Maps the Application error model onto HTTP (D1 — the mapping is all the adapter does with it).
/// Every failure leaves through here, so the shape a client parses is the same one everywhere.
/// </summary>
public static class ErrorHandling
{
    public static IApplicationBuilder UseExpensesErrors(this WebApplication app)
        => app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Expenses.Api.Errors");

            if (feature?.Error is ExpensesException expected)
            {
                int status = StatusFor(expected.Error.Code);
                logger.LogInformation(
                    "Rejected {Method} {Path}: {Code}",
                    context.Request.Method,
                    context.Request.Path,
                    expected.Error.Code);

                await Write(context, status, new ErrorResponse(
                    expected.Error.Code,
                    expected.Error.Message,
                    expected.Error.Fields));

                return;
            }

            // Anything else is ours, not the caller's. The correlation identifier is what ties the
            // response to the log entry; the response itself carries no detail at all.
            string correlationId = context.TraceIdentifier;
            logger.LogError(
                feature?.Error,
                "Unhandled failure on {Method} {Path}. Correlation {CorrelationId}.",
                context.Request.Method,
                context.Request.Path,
                correlationId);

            await Write(context, StatusCodes.Status500InternalServerError, new ErrorResponse(
                "internal_error",
                "The request could not be completed. Quote the correlation identifier when reporting it.",
                new Dictionary<string, object?>(),
                correlationId));
        }));

    /// <summary>
    /// Conventional status codes, decided by what the code means rather than by where it was
    /// raised: a missing thing is 404, a thing that already exists in a conflicting way is 409,
    /// and everything else the caller can fix is 400.
    /// </summary>
    private static int StatusFor(string code) => code switch
    {
        _ when code.EndsWith(".not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,

        ApplicationErrors.CategoryDuplicateCode
            or ApplicationErrors.MerchantDuplicateTaxId => StatusCodes.Status409Conflict,

        ApplicationErrors.ReceiptImageTooLarge => StatusCodes.Status413PayloadTooLarge,
        ApplicationErrors.ReceiptImageUnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,

        _ => StatusCodes.Status400BadRequest,
    };

    private static async Task Write(HttpContext context, int status, ErrorResponse error)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(error);
    }
}
