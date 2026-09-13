namespace Expenses.Desktop.Core.Ledger;

/// <summary>Where an attached receipt's extraction has got to. Travels as its name.</summary>
public enum ExtractionState
{
    Extracted,
    NeedsReview,
    Failed,
}
