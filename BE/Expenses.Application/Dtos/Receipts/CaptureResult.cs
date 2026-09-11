using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// What capturing an image produced. Nothing here is held server-side (D12): the caller carries
/// this forward and resubmits what it needs вЂ” the temporary key, and whatever of this it wants to
/// assert unchanged or edited вЂ” when it confirms.
/// </summary>
public sealed record CaptureResult(
    Guid TempKey,
    Receipt.ExtractionState State,
    string? FailureReason,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    FiscalIdentifiers Supplied,
    FiscalIdentifiers Extracted,
    Receipt.FiscalSource FiscalSource,

    /// <summary>
    /// The receipt's fiscal QR payload, verbatim, as supplied or as decoded during this capture.
    /// Carried back for the caller to resubmit at confirmation, which is where a receipt first
    /// exists to retain it (D32).
    /// </summary>
    string? FiscalPayload = null);
