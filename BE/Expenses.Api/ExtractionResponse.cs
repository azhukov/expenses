using Expenses.Application.Dtos;

namespace Expenses.Api;

/// <summary>
/// One extraction, as a reviewer needs to read it: the stages that ran, the stage that produced
/// each value, every arithmetic check with its outcome, and the fiscal corroboration state (D20).
/// </summary>
public sealed record ExtractionResponse(
    ReceiptView Receipt,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    bool CandidatesHeld)
{
    public static ExtractionResponse Of(ExtractionView view)
        => new(view.Receipt, view.Result, view.Validation, view.CandidatesHeld);
}
