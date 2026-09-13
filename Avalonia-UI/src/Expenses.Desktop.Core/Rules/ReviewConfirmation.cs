using Expenses.Desktop.Core.Ledger;

namespace Expenses.Desktop.Core.Rules;

/// <summary>Either the request to send, or the reason nothing can be sent yet.</summary>
public sealed record ReviewConfirmation(RecordPurchaseRequest? Request, string? Problem);
