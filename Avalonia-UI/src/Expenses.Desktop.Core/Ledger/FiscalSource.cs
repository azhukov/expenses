namespace Expenses.Desktop.Core.Ledger;

/// <summary>Where a fiscal identifier came from. <see cref="None"/> is the absence of any.</summary>
public enum FiscalSource
{
    None,
    SuppliedAtUpload,
    DecodedFromCode,
    ReadAsText,
    RetrievedFromService,
}
