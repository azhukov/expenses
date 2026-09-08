using Expenses.Application.Abstractions;
using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Extraction;

/// <summary>
/// Where a stage sits in the cascade (D20). The order is free stages first, the cheap vision tier
/// next, arithmetic validation, and the expensive tier only when validation failed.
/// </summary>
public enum ExtractionStageRole
{
    /// <summary>Free and deterministic; may produce nothing, and must cost nothing when it does.</summary>
    Opportunistic = 0,

    /// <summary>The cheap vision tier — the stage expected to produce the lines.</summary>
    Primary = 1,

    /// <summary>The expensive tier. Runs only after a failed validation.</summary>
    Fallback = 2,
}

/// <summary>
/// What a fiscal code carries, exactly as read, with no format imposed (D10). Held per source so a
/// disagreement can be reported rather than one value silently preferred.
///
/// The identifiers are only part of it: a decoded code also states the issuer, when the invoice was
/// created and what it came to, and those three are what the verification portal is asked for. They
/// travel here rather than in a parallel channel so that a later stage receives everything an
/// earlier one decoded without the cascade's seam changing shape (D22, D23).
/// </summary>
public sealed record FiscalIdentifiers(
    string? Ikof = null,
    string? Jikr = null,
    string? IssuerTaxNumber = null,
    string? CreatedAt = null,
    decimal? Total = null)
{
    public static readonly FiscalIdentifiers None = new();

    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Ikof)
        && string.IsNullOrWhiteSpace(Jikr)
        && string.IsNullOrWhiteSpace(IssuerTaxNumber)
        && string.IsNullOrWhiteSpace(CreatedAt);
}

public sealed record ExtractionStageRequest(ReceiptImageContent Image, FiscalIdentifiers Known);

/// <summary>
/// What a stage contributed. Everything is optional: a stage that produces nothing is an ordinary
/// outcome, and every later stage behaves identically whether or not it did (D20).
/// </summary>
public sealed record ExtractionStageOutcome(
    ExtractionResult? Result = null,
    FiscalIdentifiers? Fiscal = null,

    /// <summary>
    /// How the stage obtained the identifiers, where its role does not already say. A stage that
    /// asked the verification service knows something neither the code nor printed text can tell,
    /// and the receipt records that distinction (D24).
    /// </summary>
    Receipt.FiscalSource? FiscalSource = null)
{
    public static readonly ExtractionStageOutcome Nothing = new();

    public bool ProducedNothing => Result is null && (Fiscal is null || Fiscal.IsEmpty);
}

/// <summary>One step of the cascade. Ordered by <see cref="Role"/>, then by registration.</summary>
public interface IExtractionStage
{
    string Name { get; }

    ExtractionStageRole Role { get; }

    Task<ExtractionStageOutcome> Run(ExtractionStageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decoding a fiscal code from stored bytes. Measured to miss on photographed thermal receipts
/// (D20), so a miss is null rather than an exception, and nothing depends on a hit.
/// </summary>
public interface IFiscalCodeDecoder
{
    Task<FiscalIdentifiers?> Decode(ReceiptImageContent image, CancellationToken cancellationToken = default);
}

/// <summary>
/// An invoice as the verification service stated it, and the identity that came back with it. The
/// two travel together because the service answers with an identifier the fiscal code never carried
/// — the JIKR — and losing it would leave the receipt permanently unable to name itself (D24).
/// </summary>
public sealed record RetrievedInvoice(ExtractionResult Result, FiscalIdentifiers Identifiers);

/// <summary>
/// Retrieving the whole invoice from the fiscal verification service the decoded code refers to.
///
/// A miss is null, exactly as a decode miss is: the service having no record, refusing, or never
/// answering are all a stage that produced nothing, and none of them is a problem with the
/// receipt (D26).
/// </summary>
public interface IFiscalInvoiceRetrieval
{
    Task<RetrievedInvoice?> Retrieve(
        long purchaseId,
        FiscalIdentifiers decoded,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The threshold for values arithmetic cannot decide — descriptions, merchant names, category and
/// unit guesses (D20). Numeric fields have no threshold, because they are checked rather than
/// estimated.
/// </summary>
public sealed class ExtractionOptions
{
    public decimal ConfidenceThreshold { get; set; } = 0.70m;
}

/// <summary>What one run of the cascade decided, and why.</summary>
public sealed record CascadeOutcome(
    ExtractionResult? Result,
    Receipt.ExtractionState State,
    ArithmeticValidationReport? Validation,
    IReadOnlyList<string> StagesRun,
    FiscalIdentifiers Extracted,
    Receipt.FiscalSource FiscalSource,
    string? FailureReason = null,
    IReadOnlyList<string>? LowConfidenceValues = null);
