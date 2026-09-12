namespace Expenses.Api;

/// <summary>
/// The single error shape, for every failure the interface can produce. A generic failure carries
/// a correlation identifier and nothing else, so no internal detail leaks.
/// </summary>
public sealed record ErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Fields,
    string? CorrelationId = null);
