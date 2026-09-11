using Expenses.Application.Dtos;

namespace Expenses.Application.Interfaces;

/// <summary>
/// The vision boundary (D12). A placeholder adapter satisfies it in this change; a real engine
/// replaces it without anything above this port changing. Null means the engine produced nothing.
///
/// It is given the fiscal identity the run established, so that a known-true total and issuer
/// identity are available to it even where the verification service could not be reached (D23).
/// </summary>
public interface IReceiptExtractor
{
    /// <summary>Recorded on every result, so placeholder rows stay tellable apart forever.</summary>
    string EngineName { get; }

    string EngineVersion { get; }

    Task<ExtractionStepResult?> Extract(
        ReceiptImageContent image,
        FiscalIdentifiers known,
        CancellationToken cancellationToken = default);
}