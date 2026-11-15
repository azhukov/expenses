using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
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

        // Built once, and reported with whatever comes of it, so the person holding the receipt can
        // see what the site was asked beside what the QR code said.
        var sent = new FiscalPortalRequest(
            HttpMethod.Post.Method,
            new Uri(new Uri(options.BaseAddress), "/ic/api/verifyInvoice").ToString(),
            new Dictionary<string, string>
            {
                ["iic"] = iic,
                ["dateTimeCreated"] = createdAt,
                ["tin"] = tin,
            });

        var (invoice, body, failure) = await Fetch(sent, cancellationToken);

        if (failure is not null)
        {
            return Failed(iic, failure with { PortalRequest = sent });
        }

        ExtractionStepResult? mapped;

        try
        {
            mapped = invoice is null ? null : Map(purchaseId, decoded, invoice);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Readable JSON carrying a value the ledger refuses, such as a line with no name. It is
            // still an answer this cannot take.
            return Failed(
                iic,
                Unparseable($"The verification service's answer could not be taken: {exception.Message}", body)
                    with
                { PortalRequest = sent });
        }

        if (mapped is null)
        {
            // It deserialised, or deserialised to nothing, but carries no lines to take. Either way
            // this is not an invoice in the shape this was built against.
            return Failed(
                iic,
                Unparseable("The verification service answered with no invoice lines.", body) with { PortalRequest = sent });
        }

        _retrieved[iic] = invoice!;

        return new FiscalRetrieval(mapped, s_retrieved with { PortalRequest = sent });
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
        FiscalPortalRequest sent,
        CancellationToken cancellationToken)
    {
        string body;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.TimeoutMilliseconds);

            using var request = new HttpRequestMessage(new HttpMethod(sent.Method), new Uri(sent.Url))
            {
                Content = new FormUrlEncodedContent(sent.Form),
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
    /// Taken verbatim (D27), except money, which arrives in cents. The portal states each weighed line
    /// unrounded (1.696 kg × 2.30 = 3.9008) and rounds only the invoice's totals, while the ledger
    /// keeps money to the cent (D7). So each money value is rounded half away from zero, as the till
    /// prints it, and the line amounts are then brought to sum to the total the portal stated. Any
    /// cent moved onto or off a line's amount is moved on its list price too, so the line still adds
    /// up. Quantities are not money, and keep their places.
    ///
    /// The one derived value is the line's list price, which the portal states
    /// per unit while the check it feeds is a line-level one, so it is extended by the quantity вЂ”
    /// a kilogram price of 15.00 over 0.548 kg lists at 8.22, against a line amount of 8.22.
    /// </summary>
    private static ExtractionStepResult? Map(long purchaseId, FiscalIdentifiers decoded, VerifiedInvoice invoice)
    {
        if (invoice.Items is not { Count: > 0 } items)
        {
            return null;
        }

        // The amount is the one number a line cannot do without. A line the portal states with none
        // is an answer that cannot be taken, reported as such by the caller rather than guessed at.
        decimal[] exact =
        [
            .. items.Select((item, index) => item.PriceAfterVat
                ?? throw new InvalidOperationException($"Line {index + 1} states no amount.")),
        ];

        var amounts = CentRounding.ToCentsSummingTo(exact, invoice.TotalPrice);

        var candidates = items.Select((item, index) =>
        {
            decimal amount = amounts[index];

            // A list price needs both the unit price and the quantity. Without either there is
            // neither a list price nor a discount: the two are recorded together or not at all (D19).
            decimal? listPrice = ListPrice(item) is { } listed
                ? listed + (amount - CentRounding.ToCents(exact[index]))
                : null;

            return ExtractionCandidate.Propose(
                index + 1,
                item.Name ?? $"Line {index + 1}",
                amount,
                quantity: item.Quantity,
                unitPrice: item.UnitPriceAfterVat is { } unitPrice ? CentRounding.ToCents(unitPrice) : null,
                listUnitPrice: listPrice,

                // The portal's `rebate` is a percentage, not an amount, and the unit price is the one
                // before it. The amount taken off is what separates the list price from the amount,
                // both already in cents, so the line adds up however each was rounded. A unit price the
                // portal states to four places can extend to a cent under the amount on an undiscounted
                // line, which is no discount rather than a negative one.
                discountAmount: listPrice is { } list ? Math.Max(0m, list - amount) : null,
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
                    ExtractedValues.TaxRatePercent));
        });

        return ExtractionStepResult.From(
            purchaseId,
            Stage,
            "1.0",
            candidates,
            stepsRun: [Stage],
            total: invoice.TotalPrice is { } total ? CentRounding.ToCents(total) : null,

            // A rate only where the invoice has one. This receipt carries lines at 21% and at 7%,
            // and stating either as the invoice's rate would be inventing a fact the portal did
            // not state вЂ” the tax amount it did state stands on its own.
            taxRatePercent: invoice.SameTaxes is { Count: 1 } ? invoice.SameTaxes[0].VatRate : null,
            taxAmount: invoice.TotalVATAmount is { } tax ? CentRounding.ToCents(tax) : null,
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

    /// <summary>
    /// The line's list price, in cents, extended by its quantity. Normally <c>unitPriceAfterVat</c>,
    /// which is the price before any rebate. Aroma's till states it after the rebate instead (8.50
    /// less 41.29% arrives as 4.99, the line's amount), and keeps the list price only in
    /// <c>unitPriceBeforeVat</c> (7.0248, which is 8.50 at 21%). So on a line with a rebate the
    /// pre-VAT unit price is grossed up by the line's rate, and the larger of the two list prices is
    /// taken. A rebate never raises a price, so the larger one is the price before it. Lines without
    /// a rebate keep the after-VAT price as they always have, because grossing up a unit price stated
    /// to four places could move it a cent.
    /// </summary>
    private static decimal? ListPrice(Item item)
    {
        if (item.UnitPriceAfterVat is not { } unit || item.Quantity is not { } quantity)
        {
            return null;
        }

        decimal listed = CentRounding.ToCents(unit * quantity);

        if (item.Rebate is not > 0m
            || item.UnitPriceBeforeVat is not { } beforeVat
            || item.VatRate is not { } rate)
        {
            return listed;
        }

        return Math.Max(listed, CentRounding.ToCents(beforeVat * (1m + (rate / 100m)) * quantity));
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

    /// <summary>
    /// One line. Every number is optional, because tills differ in what they state: IDEA's states no
    /// rebate at all where Megapromet's states a zero one, and the portal answers <c>null</c> for it.
    /// A non-nullable field would refuse the whole invoice over a value nothing here needs. The
    /// rebate is never taken as an amount: it is a percentage, and the discount is derived from the
    /// amounts. It only marks a line whose list price may need recovering (see ListPrice).
    /// </summary>
    private sealed record Item
    {
        public string? Name { get; init; }

        public string? Unit { get; init; }

        public decimal? Quantity { get; init; }

        public decimal? UnitPriceBeforeVat { get; init; }

        public decimal? UnitPriceAfterVat { get; init; }

        public decimal? PriceAfterVat { get; init; }

        /// <summary>A percentage. Read only to tell a discounted line apart; never taken as an amount.</summary>
        public decimal? Rebate { get; init; }

        public decimal? VatRate { get; init; }
    }

    private sealed record TaxGroup
    {
        public decimal? VatRate { get; init; }
    }
}
