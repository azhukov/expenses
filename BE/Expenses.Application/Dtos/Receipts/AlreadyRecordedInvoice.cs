namespace Expenses.Application.Dtos;

/// <summary>
/// The purchase a capture's invoice appears to be recorded against already. A warning for the
/// person reviewing the capture, never a refusal: they may confirm through it (D39).
/// </summary>
public sealed record AlreadyRecordedInvoice(long PurchaseId, DateTime OccurredAt);
