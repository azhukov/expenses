using Expenses.Domain.Entities;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Dtos;

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
