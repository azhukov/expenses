namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// The single error shape every failure of the interface produces. Its message is written for a
/// person, so it is what the client shows rather than a vocabulary of its own.
/// </summary>
public sealed record ErrorResponse(string? Code, string? Message, string? CorrelationId);
