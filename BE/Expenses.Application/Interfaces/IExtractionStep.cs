using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>
/// One step of extraction. Every step takes the same request and returns the same result, and the
/// run reaches a step only when every step before it read no lines (D28). Order is registration
/// order: with decoding folded into the fiscal step, there is nothing left for a role enum to rank.
///
/// Null means the step produced nothing at all. A step that established fiscal identity without
/// reading lines returns a result with no candidates, so that what it found reaches the step behind
/// it (D29).
/// </summary>
public interface IExtractionStep
{
    string Name { get; }

    /// <summary>
    /// Whether the step needs the image. A run with no image — a payload captured on its own, or a
    /// purchase read from one being re-run — never reaches a step that does (D37).
    /// </summary>
    bool ReadsImage { get; }

    Task<ExtractionStepResult?> Run(ExtractionStepRequest request, CancellationToken cancellationToken = default);
}
