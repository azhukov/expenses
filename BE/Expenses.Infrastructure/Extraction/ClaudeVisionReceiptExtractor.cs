using System.Globalization;
using System.Text;
using System.Text.Json;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
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
          "occurred_at": string or null, the date and time of the purchase as printed, written as
            YYYY-MM-DDTHH:MM:SS with no time zone (seconds 00 if not printed, time 00:00:00 if only
            a date is printed),
          "total": number or null, the amount actually paid,
          "tax_rate_percent": number or null, the VAT rate if the receipt states a single one,
          "tax_amount": number or null, the total VAT amount,
          "lines": [
            {
              "description": string or null,
              "description_confidence": number or null, from 0 to 1,
              "quantity": number or null,
              "unit": string or null, the unit as printed (e.g. kom, kg, l),
              "unit_confidence": number or null, your confidence in the unit guess,
              "unit_price": number or null, the price per unit actually charged,
              "list_unit_price": number or null, the price per unit before any discount,
              "discount_amount": number or null, the amount taken off the whole line, only if the
                receipt prints the discount as an amount,
              "discount_percent": number or null, the discount as a percentage, only if the receipt
                prints it as one (e.g. "Popust: 47,19%" is 47.19),
              "tax_rate_percent": number or null, this line's own VAT rate if the receipt states one,
              "amount": number or null, the line's paid amount
            }
          ]
        }

        Read every value from what the image actually shows; never invent a value the receipt does
        not display. The photograph may be blurred, cropped, creased or badly lit: extract everything
        you can read and use null for anything you cannot - a whole field, or one value on a line.
        Include a line whenever you can read either its description or its amount. If no line is
        legible, return "lines": [] with whatever else you could read. Never guess to fill a gap. Copy numbers with every decimal place printed (2,2886 is 2.2886) and do not
        compute or round them yourself.
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

        if (extraction is null)
        {
            return null;
        }

        // A line with neither its text nor its amount readable is not a line anyone can review.
        List<LinePayload> lines =
            [.. (extraction.Lines ?? []).Where(line => !string.IsNullOrWhiteSpace(line.Description) || line.Amount is not null)];

        var occurredAt = ParseOccurrence(extraction.OccurredAt);

        // Nothing at all read off the image is the one outcome that is still no result.
        if (lines.Count == 0
            && extraction.MerchantName is null
            && extraction.Total is null
            && extraction.TaxAmount is null
            && occurredAt is null)
        {
            return null;
        }

        // Read as printed, then brought to cents the same way the portal's lines are (D7): a till
        // prints weighed lines to four places, a list price per unit and a discount as a
        // percentage, while the ledger and the oracle work in cents and per line.
        var money = PrintedLineMoney.ToCents(
            [
                .. lines.Select(line => new PrintedLine(
                    line.Amount,
                    line.Quantity,
                    line.UnitPrice,
                    line.ListUnitPrice,
                    line.DiscountAmount,
                    line.DiscountPercent)),
            ],
            extraction.Total);

        var candidates = lines.Select((line, index) => ExtractionCandidate.Propose(
            index + 1,
            line.Description,
            money[index].Amount,
            quantity: line.Quantity,
            unitPrice: money[index].UnitPrice,
            listUnitPrice: money[index].ListPrice,
            discountAmount: money[index].Discount,
            taxRatePercent: line.TaxRatePercent,
            unitRaw: line.Unit,
            provenance: Provenance(
                (ExtractedValues.Description, line.Description),
                (ExtractedValues.Amount, money[index].Amount),
                (ExtractedValues.Quantity, line.Quantity),
                (ExtractedValues.UnitPrice, money[index].UnitPrice),
                (ExtractedValues.ListUnitPrice, money[index].ListPrice),
                (ExtractedValues.DiscountAmount, money[index].Discount),
                (ExtractedValues.UnitGuess, line.Unit),
                (ExtractedValues.TaxRatePercent, line.TaxRatePercent)),
            reportedConfidence: LineConfidence(line)));

        return ExtractionStepResult.From(
            purchaseId,
            EngineName,
            EngineVersion,
            candidates,
            total: extraction.Total is { } total ? CentRounding.ToCents(total) : null,
            taxRatePercent: extraction.TaxRatePercent,
            taxAmount: extraction.TaxAmount is { } tax ? CentRounding.ToCents(tax) : null,
            merchantName: extraction.MerchantName,
            occurredAt: occurredAt,
            provenance: Provenance(
                (ExtractedValues.Total, extraction.Total),
                (ExtractedValues.TaxRatePercent, extraction.TaxRatePercent),
                (ExtractedValues.TaxAmount, extraction.TaxAmount),
                (ExtractedValues.MerchantName, extraction.MerchantName),
                (ExtractedValues.OccurredAt, occurredAt)),
            reportedConfidence: ResultConfidence(extraction));
    }

    /// <summary>
    /// Read leniently and on its own: a date the model wrote in some other shape costs the receipt
    /// its date, never its lines. Whatever is read is kept as wall-clock time, unconverted (D5).
    /// </summary>
    private static DateTime? ParseOccurrence(string? printed)
    {
        if (string.IsNullOrWhiteSpace(printed))
        {
            return null;
        }

        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];

        if (DateTime.TryParseExact(printed.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return DateTime.SpecifyKind(exact, DateTimeKind.Unspecified);
        }

        // An offset the prompt asked it not to write is dropped rather than applied: the receipt
        // printed local time, and that is what the ledger keeps.
        return DateTimeOffset.TryParse(printed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset)
            ? DateTime.SpecifyKind(offset.DateTime, DateTimeKind.Unspecified)
            : null;
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

    /// <summary>
    /// Only the values actually read are attributed to this stage: a value left empty on a poor
    /// photograph was produced by nobody, and claiming it would tell review it came from vision.
    /// </summary>
    private IReadOnlyDictionary<string, string> Provenance(params (string Name, object? Value)[] values)
        => values
            .Where(value => value.Value switch
            {
                null => false,
                string text => !string.IsNullOrWhiteSpace(text),
                _ => true,
            })
            .ToDictionary(value => value.Name, _ => stageName);

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

        public string? OccurredAt { get; init; }

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

        public decimal? DiscountPercent { get; init; }

        public decimal? TaxRatePercent { get; init; }

        public decimal? Amount { get; init; }
    }
}
