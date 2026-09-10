using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>One step of the cascade. Ordered by <see cref="Role"/>, then by registration.</summary>
public interface IExtractionStage
{
    string Name { get; }

    ExtractionStageRole Role { get; }

    Task<ExtractionStageOutcome> Run(ExtractionStageRequest request, CancellationToken cancellationToken = default);
}
