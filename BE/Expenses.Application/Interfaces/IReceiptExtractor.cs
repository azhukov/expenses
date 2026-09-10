using Expenses.Application.Dtos;
using Expenses.Domain.Extraction;

namespace Expenses.Application.Interfaces;

/// <summary>
/// The vision boundary (D12). A placeholder adapter satisfies it in this change; a real engine
/// replaces it without anything above this port changing. Null means the engine produced nothing.
/// </summary>
public interface IReceiptExtractor
{
    /// <summary>Recorded on every result, so placeholder rows stay tellable apart forever.</summary>
    string EngineName { get; }

    string EngineVersion { get; }

    Task<ExtractionResult?> Extract(ReceiptImageContent image, CancellationToken cancellationToken = default);
}
