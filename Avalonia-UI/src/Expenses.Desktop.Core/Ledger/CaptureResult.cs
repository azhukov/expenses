namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// What capturing an image produced. Nothing here is held server-side: the client carries it
/// forward and resubmits the parts confirmation needs, so it is kept untouched beside the user's
/// edits and never re-requested.
/// </summary>
public sealed record CaptureResult(
    string TempKey,
    ExtractionState State,
    string? FailureReason,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    FiscalIdentifiers? Supplied,
    FiscalIdentifiers? Extracted,
    FiscalSource FiscalSource,
    string? FiscalPayload);
