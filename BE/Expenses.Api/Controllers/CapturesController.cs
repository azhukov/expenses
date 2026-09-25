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
    /// A fiscal QR payload is a verification address of roughly two hundred characters. The bound
    /// is generous against that and still keeps a hostile form field away from the parser: the
    /// payload is untrusted input from here on, and the parser is what reads it (D30).
    /// </summary>
    public const int MaximumFiscalPayloadLength = 2048;

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

        // What a client read from the receipt's fiscal QR, accepted without extraction having run.
        // The raw payload rather than parsed fields, because the verification service needs the
        // issuer tax number and the creation timestamp as well as the invoice code, and a contract
        // of parsed fields could carry only the last of those (D30).
        string? payload = form["fiscalQr"];

        if (payload is { Length: > MaximumFiscalPayloadLength })
        {
            throw ExpensesException.For(
                ApplicationErrors.ReceiptFiscalPayloadTooLong,
                $"A fiscal QR payload may be at most {MaximumFiscalPayloadLength} characters.",
                ("length", payload.Length),
                ("maximumLength", MaximumFiscalPayloadLength));
        }

        // Parsed at the edge, by the one parser that reads this format, so the pipeline behind it
        // never handles a raw string and a client never has to know what a fiscal identifier is.
        // An unrecognised payload yields no identifiers rather than an error: it is untrusted input
        // and says nothing about whether the image is a receipt.
        var supplied = string.IsNullOrWhiteSpace(payload)
            ? FiscalIdentifiers.None
            : FiscalIdentity.From(payload);

        var result = await receipts.Capture(buffer.ToArray(), supplied, payload, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Captures a fiscal QR payload on its own, as a client that read the code live sends it, and
    /// runs extraction against it synchronously (D37). Nothing is stored, so the response carries no
    /// temporary key; the payload is what identifies the capture at confirmation (D38).
    /// </summary>
    [HttpPost("capture-fiscal")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [EndpointName("CaptureFiscalReceipt")]
    public async Task<ActionResult<CaptureResult>> CaptureFiscal(
        [FromBody] FiscalCaptureRequest request,
        [FromServices] ReceiptService receipts,
        CancellationToken cancellationToken)
    {
        // The same bound as beside an image, checked before the parser sees it (D30).
        if (request.Payload is { Length: > MaximumFiscalPayloadLength })
        {
            throw ExpensesException.For(
                ApplicationErrors.ReceiptFiscalPayloadTooLong,
                $"A fiscal QR payload may be at most {MaximumFiscalPayloadLength} characters.",
                ("length", request.Payload.Length),
                ("maximumLength", MaximumFiscalPayloadLength));
        }

        return Ok(await receipts.CaptureFiscal(request.Payload ?? string.Empty, cancellationToken));
    }
}
