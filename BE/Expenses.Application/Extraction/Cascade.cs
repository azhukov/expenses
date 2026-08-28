using Expenses.Application.Abstractions;
using Expenses.Domain;
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
/// Fiscal identifiers exactly as read, with no format imposed (D10). Held per source so a
/// disagreement can be reported rather than one value silently preferred.
/// </summary>
public sealed record FiscalIdentifiers(string? Ikof = null, string? Jikr = null)
{
    public static readonly FiscalIdentifiers None = new();

    public bool IsEmpty => string.IsNullOrWhiteSpace(Ikof) && string.IsNullOrWhiteSpace(Jikr);
}

public sealed record ExtractionStageRequest(ReceiptImageContent Image, FiscalIdentifiers Known);

/// <summary>
/// What a stage contributed. Everything is optional: a stage that produces nothing is an ordinary
/// outcome, and every later stage behaves identically whether or not it did (D20).
/// </summary>
public sealed record ExtractionStageOutcome(ExtractionResult? Result = null, FiscalIdentifiers? Fiscal = null)
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
