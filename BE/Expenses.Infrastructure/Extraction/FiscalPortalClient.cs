using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure.Extraction;

/// <summary>
/// The fiscal verification service, as read out of the portal's own JavaScript bundle: a form-encoded
/// POST of `iic`, `dateTimeCreated` and `tin` to `/ic/api/verifyInvoice`, answering with the whole
/// invoice. It is undocumented and may change without notice, which is the other reason every
/// failure here is nothing rather than an error (D26).
///
/// Every failure (no record, a refusal, a timeout, a shape this has never seen) returns no invoice.
/// The portal is on the critical path of a better answer, never of an answer at all. Each failure is
/// still classified here, at the only place that can tell them apart, so that the capture can say
/// which one happened (D43).
/// </summary>
internal sealed class FiscalPortalClient(
    IHttpClientFactory clients,
    PortalOptions options,
    ILogger<FiscalPortalClient> logger) : IFiscalInvoiceRetrieval
{
    public const string ClientName = "fiscal-portal";

    public const string Stage = "fiscal-portal";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private static readonly FiscalDiagnosis s_retrieved
        = new(FiscalDiagnosisKind.Retrieved, "The invoice was retrieved from the fiscal verification service.");

    /// <summary>
    /// Answers already given, by the invoice code they were given for. Re-running extraction on a
    /// receipt asks the portal nothing it has already said, which matters more here than for a
    /// local computation: the request leaves the building. Only answers that mapped onto an invoice
    /// are kept, so a failure is asked about afresh next time (D43).
    /// </summary>
    private readonly ConcurrentDictionary<string, VerifiedInvoice> _retrieved = new(StringComparer.Ordinal);

    public async Task<FiscalRetrieval> Retrieve(
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
            string[] missing =
            [
                .. new[] { ("iic", decoded.Ikof), ("tin", decoded.IssuerTaxNumber), ("crtd", decoded.CreatedAt) }
                    .Where(parameter => string.IsNullOrEmpty(parameter.Item2))
                    .Select(parameter => parameter.Item1),
            ];

            return Failed(
                decoded.Ikof,
                new FiscalDiagnosis(
                    FiscalDiagnosisKind.IncompleteIdentifiers,
                    $"The fiscal code is missing {string.Join(", ", missing)}, so the verification service cannot be asked.",
                    MissingParameters: missing));
        }

        if (_retrieved.TryGetValue(iic, out var known))
        {
            return new FiscalRetrieval(Map(purchaseId, decoded, known), s_retrieved);
        }

        var (invoice, body, failure) = await Fetch(iic, tin, createdAt, cancellationToken);

        if (failure is not null)
        {
            return Failed(iic, failure);
        }

        var mapped = invoice is null ? null : Map(purchaseId, decoded, invoice);

        if (mapped is null)
        {
            // It deserialised, or deserialised to nothing, but carries no lines to take. Either way
            // this is not an invoice in the shape this was built against.
            return Failed(iic, Unparseable("The verification service answered with no invoice lines.", body));
        }

        _retrieved[iic] = invoice!;

        return new FiscalRetrieval(mapped, s_retrieved);
    }

    private static FiscalDiagnosis Unparseable(string detail, string body)
        => new(FiscalDiagnosisKind.UnparseableResponse, detail, ResponseExcerpt: FiscalDiagnosis.Excerpt(body));

    /// <summary>
    /// No invoice, and why. Logged at warning, so that it reaches the durable log without debug
    /// logging turned on. A capture shows the user one of these, and the log is where the operator
    /// finds the same thing afterwards (D46).
    /// </summary>
    private FiscalRetrieval Failed(string? iic, FiscalDiagnosis diagnosis)
    {
        logger.LogWarning(
            "No invoice from the fiscal verification service for invoice {Iic}: {Kind} {Status} {Detail} {Excerpt}",
            iic,
            diagnosis.Kind,
            diagnosis.PortalStatus,
            diagnosis.Detail,
            diagnosis.ResponseExcerpt);

        return new FiscalRetrieval(null, diagnosis);
    }

    /// <summary>
    /// One request to the portal. It answers with the invoice and the body it came from, or with the
    /// failure it ran into. Cancellation by the caller still propagates. Only the portal's own time
    /// budget running out counts as the portal being unreachable.
    /// </summary>
    private async Task<(VerifiedInvoice? Invoice, string Body, FiscalDiagnosis? Failure)> Fetch(
        string iic,
        string tin,
        string createdAt,
        CancellationToken cancellationToken)
    {
        string body;

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
                return (null, string.Empty, new FiscalDiagnosis(
                    FiscalDiagnosisKind.PortalRejected,
                    $"The verification service answered {(int)response.StatusCode} {response.ReasonPhrase}.",
                    PortalStatus: (int)response.StatusCode));
            }

            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (HttpRequestException exception)
        {
            return (null, string.Empty, new FiscalDiagnosis(
                FiscalDiagnosisKind.PortalUnreachable,
                $"The verification service could not be reached: {exception.Message}"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, string.Empty, new FiscalDiagnosis(
                FiscalDiagnosisKind.PortalUnreachable,
                $"The verification service timed out after {options.TimeoutMilliseconds} ms."));
        }

        // An empty body is the service saying it has no record, not a malformed answer.
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, body, new FiscalDiagnosis(
                FiscalDiagnosisKind.NoRecord,
                "The verification service has no record of this invoice."));
        }

        try
        {
            return (JsonSerializer.Deserialize<VerifiedInvoice>(body, s_json), body, null);
        }
        catch (JsonException exception)
        {
            return (null, body, Unparseable($"The verification service's answer is not readable JSON: {exception.Message}", body));
        }
    }

    /// <summary>
    /// Taken verbatim (D27). The one derived value is the line's list price, which the portal states
    /// per unit while the check it feeds is a line-level one, so it is extended by the quantity вЂ”
    /// a kilogram price of 15.00 over 0.548 kg lists at 8.22, against a line amount of 8.22.
    /// </summary>
    private static ExtractionStepResult? Map(long purchaseId, FiscalIdentifiers decoded, VerifiedInvoice invoice)
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

        return ExtractionStepResult.From(
            purchaseId,
            Stage,
            "1.0",
            candidates,
            stepsRun: [Stage],
            total: invoice.TotalPrice,

            // A rate only where the invoice has one. This receipt carries lines at 21% and at 7%,
            // and stating either as the invoice's rate would be inventing a fact the portal did
            // not state вЂ” the tax amount it did state stands on its own.
            taxRatePercent: invoice.SameTaxes is { Count: 1 } ? invoice.SameTaxes[0].VatRate : null,
            taxAmount: invoice.TotalVATAmount,
            merchantName: invoice.Seller?.Name,
            merchantTaxId: invoice.Seller?.IdNum,
            provenance: Provenance(
                ExtractedValues.Total,
                ExtractedValues.TaxAmount,
                ExtractedValues.TaxRatePercent,
                ExtractedValues.MerchantName),

            // The JIKR arrives here and nowhere else. Everything else the code already carried is
            // carried through unchanged, so nothing decoded is lost by having asked (D24).
            fiscal: decoded with { Jikr = invoice.Fic },
            fiscalSource: FiscalInvoice.FiscalSource.RetrievedFromService);
    }

    private static IReadOnlyDictionary<string, string> Provenance(params string[] values)
        => values.ToDictionary(value => value, _ => Stage);

    /// <summary>
    /// The response shape, as observed on one invoice. Anything the portal returns that is not named
    /// here is ignored rather than guessed at, and anything named here that is missing arrives as
    /// its default вЂ” a refund or a corrected invoice may well look different from the one receipt
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
