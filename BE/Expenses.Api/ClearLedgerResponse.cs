namespace Expenses.Api;

/// <summary>
/// How many purchases a clear deleted. Receipt files the clear could not remove are logged by the
/// host, not returned: their storage keys are an internal detail (D3 of add-clear-all-expenses).
/// </summary>
public sealed record ClearLedgerResponse(int Deleted);
