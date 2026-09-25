namespace Expenses.Api;

/// <summary>
/// A fiscal QR payload captured on its own, verbatim as the code carries it. Nullable so that a
/// missing payload reaches the use case and is refused there with its own code, rather than by
/// binding with a generic one (D37).
/// </summary>
public sealed record FiscalCaptureRequest(string? Payload);
