using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// What capturing an image produced. Nothing here is held server-side (D12): the caller carries
/// this forward and resubmits what it needs вЂ” the temporary key, and whatever of this it wants to
/// assert unchanged or edited, when it confirms. A fiscal capture stored no image and carries no
/// key; its payload identifies it at confirmation instead (D38).
/// </summary>
public sealed record CaptureResult(
    Guid? TempKey,
    Purchase.ExtractionState State,
    string? FailureReason,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    FiscalIdentifiers Supplied,
    FiscalIdentifiers Extracted,
    FiscalInvoice.FiscalSource FiscalSource,

    /// <summary>
    /// The receipt's fiscal QR payload, verbatim, as supplied or as decoded during this capture.
    /// Carried back for the caller to resubmit at confirmation, which is where a receipt first
    /// exists to retain it (D32).
    /// </summary>
    string? FiscalPayload = null,

    /// <summary>
    /// The purchase already carrying the invoice this capture established, where one does. A warning
    /// only: it changes neither the outcome nor whether the capture can be confirmed (D39).
    /// </summary>
    AlreadyRecordedInvoice? AlreadyRecorded = null);
