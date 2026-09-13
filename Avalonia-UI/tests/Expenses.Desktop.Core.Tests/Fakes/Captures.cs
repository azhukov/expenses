using Expenses.Desktop.Core.Ledger;

namespace Expenses.Desktop.Core.Tests.Fakes;

/// <summary>Capture responses in each of the shapes the review screen has to handle.</summary>
public static class Captures
{
    public static readonly FiscalIdentifiers NoFiscal = new(null, null, null, null, null);

    public static ExtractionCandidateView Candidate(
        int lineNumber,
        string description,
        decimal amount,
        decimal? quantity = null,
        long? categoryId = null,
        long? unitId = null,
        string? categoryRaw = null,
        string? unitRaw = null,
        IReadOnlyDictionary<string, decimal>? confidence = null)
    {
        return new ExtractionCandidateView(
            lineNumber, description, amount, quantity, null, null, null, null, categoryRaw, unitRaw, categoryId, unitId,
            new Dictionary<string, string>(), confidence ?? new Dictionary<string, decimal>());
    }

    public static CaptureResult Extracted(
        IReadOnlyList<ExtractionCandidateView> candidates,
        decimal? total = null,
        string? merchantName = null,
        ExtractionState state = ExtractionState.Extracted,
        IReadOnlyList<ArithmeticCheck>? checks = null,
        IReadOnlyDictionary<string, decimal>? confidence = null,
        DateTime? fiscalCreatedAt = null,
        FiscalSource fiscalSource = FiscalSource.None,
        string? fiscalPayload = null,
        string tempKey = "tmp-1")
    {
        var result = new ExtractionResultView(
            "cascade", "1", ["vision"], candidates, total, null, null, merchantName, null,
            new Dictionary<string, string>(), confidence ?? new Dictionary<string, decimal>());

        return new CaptureResult(
            tempKey,
            state,
            null,
            result,
            new ArithmeticValidationReport(checks ?? []),
            NoFiscal,
            fiscalCreatedAt is null ? NoFiscal : new FiscalIdentifiers("IKOF", "JIKR-1", "02005328", fiscalCreatedAt, total),
            fiscalSource,
            fiscalPayload);
    }

    public static CaptureResult Failed(string reason = "No text could be read from the image.", string tempKey = "tmp-failed")
    {
        return new CaptureResult(tempKey, ExtractionState.Failed, reason, null, null, NoFiscal, NoFiscal, FiscalSource.None, null);
    }

    public static ArithmeticCheck Check(string description, CheckOutcome outcome)
    {
        return new ArithmeticCheck("Check", outcome, description, new Dictionary<string, decimal?>());
    }
}
