using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Expenses.Application.Extraction;
using Expenses.Domain.Extraction;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Extraction;

internal sealed class PortalOptions
{
    /// <summary>
    /// The one national fiscalisation portal every receipt in this design is assumed to be verified
    /// by. Configured rather than compiled in so a test can point it somewhere that is not a
    /// government service (D26).
    /// </summary>
    public string BaseAddress { get; set; } = "https://mapr.tax.gov.me";

    /// <summary>
    /// Bounded, because ingestion may never wait on someone else's server. When it runs out the
    /// stage produced nothing, which is an outcome the cascade already handles everywhere.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 5000;
}

/// <summary>
/// The fiscal verification service, as read out of the portal's own JavaScript bundle: a form-encoded
/// POST of `iic`, `dateTimeCreated` and `tin` to `/ic/api/verifyInvoice`, answering with the whole
/// invoice. It is undocumented and may change without notice, which is the other reason every
/// failure here is nothing rather than an error (D26).
///
/// Every failure — no record, a refusal, a timeout, a shape this has never seen — returns null. The
/// portal is on the critical path of a better answer, never of an answer at all.
/// </summary>
internal sealed class FiscalPortalClient(
    IHttpClientFactory clients,
    PortalOptions options,
    ILogger<FiscalPortalClient> logger) : IFiscalInvoiceRetrieval
{
    public const string ClientName = "fiscal-portal";

    public const string Stage = "fiscal-portal";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Answers already given, by the invoice code they were given for. Re-running extraction on a
    /// receipt asks the portal nothing it has already said, which matters more here than for a
    /// local computation: the request leaves the building.
    /// </summary>
    private readonly ConcurrentDictionary<string, VerifiedInvoice> _retrieved = new(StringComparer.Ordinal);

    public async Task<RetrievedInvoice?> Retrieve(
        long purchaseId,
        FiscalIdentifiers decoded,
        CancellationToken cancellationToken = default)
    {
        // The portal is asked by invoice, issuer and moment. Without all three there is nothing to
        // ask about, and a decode that yielded less than that is simply a stage that produced less.
        if (decoded.Ikof is not { Length: > 0 } iic
            || decoded.IssuerTaxNumber is not { Length: > 0 } tin
            || decoded.CreatedAt is not { Length: > 0 } createdAt)
        {
            return null;
        }

        var invoice = _retrieved.TryGetValue(iic, out var known)
            ? known
            : await Fetch(iic, tin, createdAt, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        _retrieved[iic] = invoice;

        return Map(purchaseId, decoded, invoice);
    }

    private async Task<VerifiedInvoice?> Fetch(
        string iic,
        string tin,
        string createdAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.TimeoutMilliseconds);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(options.BaseAddress), "/ic/api/verifyInvoice"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["iic"] = iic,
                    ["dateTimeCreated"] = createdAt,
                    ["tin"] = tin,
                }),
            };

            using var client = clients.CreateClient(ClientName);
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "The fiscal verification service answered {Status} for invoice {Iic}; continuing without it.",
                    response.StatusCode,
                    iic);

                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            // An empty body is the service saying it has no record, not a malformed answer.
            return string.IsNullOrWhiteSpace(body)
                ? null
                : JsonSerializer.Deserialize<VerifiedInvoice>(body, Json);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or OperationCanceledException or JsonException)
        {
            // Unreachable, too slow, or answering with something this has never seen: all of them
            // are a stage that produced nothing, and none of them is reported about the receipt.
            logger.LogDebug(
                exception,
                "The fiscal verification service could not be consulted for invoice {Iic}; continuing without it.",
                iic);

            return null;
        }
    }

    /// <summary>
    /// Taken verbatim (D27). The one derived value is the line's list price, which the portal states
    /// per unit while the check it feeds is a line-level one, so it is extended by the quantity —
    /// a kilogram price of 15.00 over 0.548 kg lists at 8.22, against a line amount of 8.22.
    /// </summary>
    private static RetrievedInvoice? Map(long purchaseId, FiscalIdentifiers decoded, VerifiedInvoice invoice)
    {
        if (invoice.Items is not { Count: > 0 } items)
        {
            return null;
        }

        var candidates = items.Select((item, index) => ExtractionCandidate.Propose(
            index + 1,
            item.Name ?? $"Line {index + 1}",
            item.PriceAfterVat,
            quantity: item.Quantity,
            unitPrice: item.UnitPriceAfterVat,
            listUnitPrice: item.UnitPriceAfterVat * item.Quantity,
            discountAmount: item.Rebate,
            taxRatePercent: item.VatRate,
            unitRaw: item.Unit,
            provenance: Provenance(
                ExtractedValues.Description,
                ExtractedValues.Amount,
                ExtractedValues.Quantity,
                ExtractedValues.UnitPrice,
                ExtractedValues.ListUnitPrice,
                ExtractedValues.DiscountAmount,
                ExtractedValues.UnitGuess,
                ExtractedValues.TaxRatePercent)));

        var result = ExtractionResult.From(
            purchaseId,
            Stage,
            "1.0",
            candidates,
            total: invoice.TotalPrice,

            // A rate only where the invoice has one. This receipt carries lines at 21% and at 7%,
            // and stating either as the invoice's rate would be inventing a fact the portal did
            // not state — the tax amount it did state stands on its own.
            taxRatePercent: invoice.SameTaxes is { Count: 1 } ? invoice.SameTaxes[0].VatRate : null,
            taxAmount: invoice.TotalVATAmount,
            merchantName: invoice.Seller?.Name,
            merchantTaxId: invoice.Seller?.IdNum,
            provenance: Provenance(
                ExtractedValues.Total,
                ExtractedValues.TaxAmount,
                ExtractedValues.TaxRatePercent,
                ExtractedValues.MerchantName));

        // The JIKR arrives here and nowhere else. Everything else the code already carried is
        // carried through unchanged, so nothing decoded is lost by having asked.
        return new RetrievedInvoice(result, decoded with { Jikr = invoice.Fic });
    }

    private static IReadOnlyDictionary<string, string> Provenance(params string[] values) =>
        values.ToDictionary(value => value, _ => Stage);

    /// <summary>
    /// The response shape, as observed on one invoice. Anything the portal returns that is not named
    /// here is ignored rather than guessed at, and anything named here that is missing arrives as
    /// its default — a refund or a corrected invoice may well look different from the one receipt
    /// this was read from.
    /// </summary>
    private sealed record VerifiedInvoice
    {
        public string? Fic { get; init; }

        public decimal? TotalPrice { get; init; }

        [JsonPropertyName("totalVATAmount")]
        public decimal? TotalVATAmount { get; init; }

        public Party? Seller { get; init; }

        public IReadOnlyList<Item>? Items { get; init; }

        public IReadOnlyList<TaxGroup>? SameTaxes { get; init; }
    }

    private sealed record Party
    {
        public string? Name { get; init; }

        public string? IdNum { get; init; }
    }

    private sealed record Item
    {
        public string? Name { get; init; }

        public string? Unit { get; init; }

        public decimal Quantity { get; init; }

        public decimal UnitPriceAfterVat { get; init; }

        public decimal PriceAfterVat { get; init; }

        public decimal Rebate { get; init; }

        public decimal VatRate { get; init; }
    }

    private sealed record TaxGroup
    {
        public decimal VatRate { get; init; }

        public decimal VatAmount { get; init; }
    }
}
