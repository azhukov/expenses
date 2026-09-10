using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Dtos;

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
