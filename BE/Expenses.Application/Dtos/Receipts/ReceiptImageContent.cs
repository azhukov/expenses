namespace Expenses.Application.Dtos;

/// <summary>The bytes of one receipt, loaded deliberately and never incidentally (D11).</summary>
#pragma warning disable CA1819 // Properties should not return arrays
public sealed record ReceiptImageContent(long PurchaseId, string ContentType, byte[] Content);
#pragma warning restore CA1819 // Properties should not return arrays
