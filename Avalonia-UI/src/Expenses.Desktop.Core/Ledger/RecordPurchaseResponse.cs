namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// A recorded purchase, with <see cref="AlreadyRecorded"/> telling the two success outcomes apart.
/// Only the fields the client reads are declared; the rest of the response is ignored.
/// </summary>
public sealed record RecordPurchaseResponse(
    long Id,
    DateTime OccurredAt,
    decimal Amount,
    bool HasReceipt,
    bool AlreadyRecorded);
