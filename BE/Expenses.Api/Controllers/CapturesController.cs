using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Expenses.Api.Controllers;

/// <summary>
/// Capturing a receipt image with no purchase behind it (D-none — new for this change). HTTP-only:
/// MCP never accepts image bytes as a tool argument, for capture or anything else.
/// </summary>
[ApiController]
[Route("receipts")]
public sealed class CapturesController : ControllerBase
{
    /// <summary>Enforced before the body is read, so an oversized upload is never buffered.</summary>
    public const long MaximumUploadBytes = 15 * 1024 * 1024;

    /// <summary>
    /// Stores an image temporarily and runs extraction against it synchronously, with no purchase
    /// referenced. The response identifies the capture by its temporary key, never by a purchase or
    /// image identifier.
    /// </summary>
    [HttpPost("capture")]
    [Produces("application/json")]
    [EndpointName("CaptureReceipt")]
    public async Task<ActionResult<CaptureResult>> Capture(
        [FromServices] ReceiptService receipts,
        CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"]
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageUnsupportedFormat,
                "The upload carried no file part named 'file'.");

        // Checked from the declared length before a byte is copied: the limit exists to stop
        // the request being buffered, so enforcing it after buffering would miss the point.
        if (file.Length > MaximumUploadBytes)
        {
            throw ExpensesException.For(
                ApplicationErrors.ReceiptImageTooLarge,
                $"A receipt image may be at most {MaximumUploadBytes / (1024 * 1024)} MB.",
                ("sizeInBytes", file.Length),
                ("maximumSizeInBytes", MaximumUploadBytes));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        // Identifiers a client decoded at capture, accepted without extraction having run (D20).
        var supplied = new FiscalIdentifiers(form["fiscalIkof"], form["fiscalJikr"]);

        var result = await receipts.Capture(buffer.ToArray(), supplied, cancellationToken);

        return Ok(result);
    }
}
