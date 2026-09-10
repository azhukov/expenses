using Expenses.Domain.Extraction;

namespace Expenses.Application.Tests.Fakes;

/// <summary>Extraction results shaped for the arithmetic oracle, built the way a receipt reads.</summary>
internal static class Results
{
    /// <summary>The worked example from D20: two discounted lines that reconcile with the total.</summary>
    public static ExtractionResult Reconciling(
        string stage,
        long receiptImageId = 1,
        string engine = "placeholder",
        IReadOnlyDictionary<string, decimal>? reportedConfidence = null)
        => ExtractionResult.From(
            receiptImageId,
            engine,
            "1.0",
            [
                ExtractionCandidate.Propose(
                    1,
                    "Sladoled Milka Mini Sticks",
                    4.49m,
                    listUnitPrice: 8.50m,
                    discountAmount: 4.01m,
                    provenance: new Dictionary<string, string> { ["amount"] = stage }),
                ExtractionCandidate.Propose(
                    2,
                    "Cokolada",
                    3.99m,
                    listUnitPrice: 7.50m,
                    discountAmount: 3.51m,
                    provenance: new Dictionary<string, string> { ["amount"] = stage }),
            ],
            total: 8.48m,
            taxRatePercent: 21m,
            taxAmount: 1.47m,
            provenance: new Dictionary<string, string> { ["total"] = stage },
            reportedConfidence: reportedConfidence);

    /// <summary>
    /// A result whose lines do not sum to its total. <paramref name="alsoBreakDiscount"/> breaks a
    /// second check as well, which is what makes two failed results comparable (D20).
    /// </summary>
    public static ExtractionResult Failing(
        string stage,
        bool alsoBreakDiscount = false,
        long receiptImageId = 1)
        => ExtractionResult.From(
            receiptImageId,
            "placeholder",
            "1.0",
            [
                ExtractionCandidate.Propose(
                    1,
                    "Sladoled Milka Mini Sticks",
                    4.49m,
                    listUnitPrice: 8.50m,
                    discountAmount: alsoBreakDiscount ? 3.01m : 4.01m,
                    provenance: new Dictionary<string, string> { ["amount"] = stage }),
                ExtractionCandidate.Propose(2, "Cokolada", 3.99m),
            ],
            total: 8.98m,
            provenance: new Dictionary<string, string> { ["total"] = stage });
}
