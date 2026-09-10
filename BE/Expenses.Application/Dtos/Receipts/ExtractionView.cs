namespace Expenses.Application.Dtos;

/// <summary>
/// A receipt together with whatever the cascade has made of it вЂ” including the arithmetic report,
/// which is what a reviewer needs in order to see why it needs reviewing (D20).
///
/// <see cref="CandidatesHeld"/> tells "no candidates are held any more" apart from "extraction
/// produced no lines": candidates do not survive a restart, and that absence is not a failure
/// (D12).
/// </summary>
public sealed record ExtractionView(
    ReceiptView Receipt,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    bool CandidatesHeld);
