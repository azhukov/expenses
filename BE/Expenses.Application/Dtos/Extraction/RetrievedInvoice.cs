using Expenses.Domain.Extraction;

namespace Expenses.Application.Dtos;

/// <summary>
/// An invoice as the verification service stated it, and the identity that came back with it. The
/// two travel together because the service answers with an identifier the fiscal code never carried
/// — the JIKR — and losing it would leave the receipt permanently unable to name itself (D24).
/// </summary>
public sealed record RetrievedInvoice(ExtractionResult Result, FiscalIdentifiers Identifiers);
