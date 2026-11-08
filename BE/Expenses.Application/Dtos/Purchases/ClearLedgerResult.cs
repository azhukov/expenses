namespace Expenses.Application.Dtos;

/// <summary>
/// What a clear did. <see cref="ReceiptFilesNotRemoved"/> names receipt files the clear could not
/// delete after it committed: they are unreferenced and safe to discard, and are reported rather
/// than thrown because the ledger has already been cleared (D3 of add-clear-all-expenses). The
/// Application ring carries no logging, so the adapter that called the clear is where they are logged.
/// </summary>
public sealed record ClearLedgerResult(int Deleted, IReadOnlyList<string> ReceiptFilesNotRemoved);
