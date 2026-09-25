using Expenses.Domain.Entities;

namespace Expenses.Application.Dtos;

/// <summary>
/// What one run of extraction produced. It reports and does not judge: whether this is
/// <c>Extracted</c> or <c>NeedsReview</c> is decided once, afterwards, by the caller that validates
/// it (D28). The one verdict here is <see cref="FailureReason"/>, which is set when no step read
/// anything at all — the only outcome that is a failure rather than a matter for review.
/// </summary>
public sealed record CascadeOutcome(
    ExtractionStepResult? Result,
    IReadOnlyList<string> StepsRun,
    FiscalIdentifiers Extracted,
    FiscalInvoice.FiscalSource FiscalSource,
    string? FailureReason = null,

    /// <summary>
    /// The fiscal QR payload the run holds — supplied with the capture, or decoded during it. Null
    /// where none was obtained (D32).
    /// </summary>
    string? Payload = null);
