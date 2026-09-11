using System.Text;
using System.Text.Json;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Domain.Extraction;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// The real vision engine (D28): the receipt image is sent to a Claude model over the Messages API
/// (`/v1/messages`, D29) as inline base64 content (D30), with a prompt asking for one JSON object
/// shaped like the cascade's own candidates (D31).
///
/// Every failure — unreachable, too slow, no credential, an answer that is not the expected shape —
/// returns null exactly as <see cref="FiscalPortalClient"/> does for the verification service (D26,
/// D32): vision is the last stage in the cascade, so a failure here is a receipt with fewer
/// candidates, never a thrown error.
/// </summary>
internal sealed class ClaudeVisionReceiptExtractor(
    IHttpClientFactory clients,
    VisionOptions options,
    string stageName,
    ILogger<ClaudeVisionReceiptExtractor> logger) : IReceiptExtractor
{
    public const string ClientName = "claude-vision";

    public const string Engine = "claude-vision";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const string Prompt = """
        You are reading a photograph of a retail receipt. Reply with exactly one JSON object and
        nothing else - no markdown fences, no commentary before or after it - matching this shape:

        {
          "merchant_name": string or null,
          "merchant_name_confidence": number from 0 to 1, your confidence in merchant_name,
          "total": number or null, the amount actually paid,
          "tax_rate_percent": number or null, the VAT rate if the receipt states a single one,
          "tax_amount": number or null, the total VAT amount,
          "lines": [
            {
              "description": string,
              "description_confidence": number from 0 to 1,
              "quantity": number or null,
              "unit": string or null, the unit as printed (e.g. kom, kg, l),
              "unit_confidence": number or null, your confidence in the unit guess,
              "unit_price": number or null,
              "list_unit_price": number or null, the price before any discount,
              "discount_amount": number or null,
              "tax_rate_percent": number or null, this line's own VAT rate if the receipt states one,
              "amount": number, the line's paid amount
            }
          ]
        }

        Read every value from what the image actually shows; never invent a value the receipt does
        not display.
        """;

    public string EngineName => Engine;

    public string EngineVersion => options.ModelId;

    public async Task<ExtractionStepResult?> Extract(
        ReceiptImageContent image,
        FiscalIdentifiers known,
        CancellationToken cancellationToken = default)
    {
        // A missing credential is not a startup failure: the vision stage may be configured with
        // none at all where it is not needed (D32).
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.TimeoutMilliseconds);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(options.BaseAddress), "/v1/messages"))
            {
                Content = new StringContent(BuildRequestBody(image), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", options.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            using var client = clients.CreateClient(ClientName);
            using var response = await client.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "The vision engine answered {Status} for purchase {PurchaseId}; continuing without it.",
                    response.StatusCode,
                    image.PurchaseId);

                return null;
            }

            string body = await response.Content.ReadAsStringAsync(timeout.Token);

            return Map(image.PurchaseId, body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or OperationCanceledException or JsonException)
        {
            // Unreachable, too slow, or answering with something this has never seen: all of them
            // are a stage that produced nothing, and none of them is reported about the receipt.
            logger.LogDebug(
                exception,
                "The vision engine could not be consulted for purchase {PurchaseId}; continuing without it.",
                image.PurchaseId);

            return null;
        }
    }

    private string BuildRequestBody(ReceiptImageContent image)
    {
        var payload = new
        {
            model = options.ModelId,
            max_tokens = 4096,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = image.ContentType,
                                data = Convert.ToBase64String(image.Content),
                            },
                        },
                        new { type = "text", text = Prompt },
                    },
                },
            },
        };

        return JsonSerializer.Serialize(payload, s_json);
    }

    /// <summary>
    /// Deserialized strictly: a body missing the expected fields is treated as "produced nothing"
    /// (D26's rule extended to vision, D31) rather than mapped with holes in it.
    /// </summary>
    private ExtractionStepResult? Map(long purchaseId, string body)
    {
        var message = JsonSerializer.Deserialize<MessagesResponse>(body, s_json);
        string? text = message?.Content?.FirstOrDefault(block => block.Type == "text")?.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var extraction = JsonSerializer.Deserialize<ExtractionPayload>(text, s_json);

        if (extraction?.Lines is not { Count: > 0 } lines)
        {
            return null;
        }

        var candidates = lines.Select((line, index) => ExtractionCandidate.Propose(
            index + 1,
            line.Description ?? $"Line {index + 1}",
            line.Amount,
            quantity: line.Quantity,
            unitPrice: line.UnitPrice,
            listUnitPrice: line.ListUnitPrice,
            discountAmount: line.DiscountAmount,
            taxRatePercent: line.TaxRatePercent,
            unitRaw: line.Unit,
            provenance: Provenance(
                ExtractedValues.Description,
                ExtractedValues.Amount,
                ExtractedValues.Quantity,
                ExtractedValues.UnitPrice,
                ExtractedValues.ListUnitPrice,
                ExtractedValues.DiscountAmount,
                ExtractedValues.UnitGuess,
                ExtractedValues.TaxRatePercent),
            reportedConfidence: LineConfidence(line)));

        return ExtractionStepResult.From(
            purchaseId,
            EngineName,
            EngineVersion,
            candidates,
            total: extraction.Total,
            taxRatePercent: extraction.TaxRatePercent,
            taxAmount: extraction.TaxAmount,
            merchantName: extraction.MerchantName,
            provenance: Provenance(
                ExtractedValues.Total,
                ExtractedValues.TaxRatePercent,
                ExtractedValues.TaxAmount,
                ExtractedValues.MerchantName),
            reportedConfidence: ResultConfidence(extraction));
    }

    /// <summary>Only values arithmetic cannot decide carry a reported confidence (D20).</summary>
    private static IReadOnlyDictionary<string, decimal>? LineConfidence(LinePayload line)
    {
        Dictionary<string, decimal>? confidence = null;

        if (line.DescriptionConfidence is { } description)
        {
            (confidence ??= [])[ExtractedValues.Description] = description;
        }

        if (line.UnitConfidence is { } unit)
        {
            (confidence ??= [])[ExtractedValues.UnitGuess] = unit;
        }

        return confidence;
    }

    private static IReadOnlyDictionary<string, decimal>? ResultConfidence(ExtractionPayload extraction)
        => extraction.MerchantNameConfidence is { } confidence
            ? new Dictionary<string, decimal> { [ExtractedValues.MerchantName] = confidence }
            : null;

    private IReadOnlyDictionary<string, string> Provenance(params string[] values)
        => values.ToDictionary(value => value, _ => stageName);

    /// <summary>The Messages API envelope: only the one content block this adapter asks for is read.</summary>
    private sealed record MessagesResponse
    {
        public IReadOnlyList<ContentBlock>? Content { get; init; }
    }

    private sealed record ContentBlock
    {
        public string? Type { get; init; }

        public string? Text { get; init; }
    }

    /// <summary>The JSON shape the prompt asks the model for (D31).</summary>
    private sealed record ExtractionPayload
    {
        public string? MerchantName { get; init; }

        public decimal? MerchantNameConfidence { get; init; }

        public decimal? Total { get; init; }

        public decimal? TaxRatePercent { get; init; }

        public decimal? TaxAmount { get; init; }

        public IReadOnlyList<LinePayload>? Lines { get; init; }
    }

    private sealed record LinePayload
    {
        public string? Description { get; init; }

        public decimal? DescriptionConfidence { get; init; }

        public decimal? Quantity { get; init; }

        public string? Unit { get; init; }

        public decimal? UnitConfidence { get; init; }

        public decimal? UnitPrice { get; init; }

        public decimal? ListUnitPrice { get; init; }

        public decimal? DiscountAmount { get; init; }

        public decimal? TaxRatePercent { get; init; }

        public decimal Amount { get; init; }
    }
}
