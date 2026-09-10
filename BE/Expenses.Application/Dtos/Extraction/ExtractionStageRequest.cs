namespace Expenses.Application.Dtos;

public sealed record ExtractionStageRequest(ReceiptImageContent Image, FiscalIdentifiers Known);
