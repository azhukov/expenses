using System.Globalization;
using Expenses.Desktop.Core.Ledger;

namespace Expenses.Desktop.Core.Rules;

/// <summary>
/// What the review screen is about, apart from how it is drawn (ported from FE/src/capture/review.ts,
/// D7): the reasons a capture wants a human, the lines the user edits, and the request confirming one
/// produces.
/// </summary>
public static class ReviewRules
{
    /// <summary>The one format the date field reads, so what is typed means one thing wherever it is typed.</summary>
    public const string DateFormat = "yyyy-MM-dd HH:mm";

    public const string DateFormatProblem = "Enter the date as year-month-day and time, for example 2026-09-13 18:05.";

    /// <summary>The API's threshold: a value it did not consider low is not flagged here either.</summary>
    private const decimal ConfidenceThreshold = 0.7m;

    /// <summary>
    /// The values no arithmetic can decide, and so the only ones extraction's reported confidence is
    /// consulted for; everything numeric is proved by the arithmetic checks instead.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> s_unverifiable = new Dictionary<string, string>
    {
        ["description"] = "the description",
        ["merchant_name"] = "the merchant name",
        ["category_guess"] = "the category",
        ["unit_guess"] = "the unit",
    };

    /// <summary>
    /// Why this capture wants a human, in the response's own words: each failed check as the check
    /// describes itself, then each value extraction was unsure of, named once.
    /// </summary>
    public static IReadOnlyList<string> Reasons(CaptureResult capture)
    {
        var failed = (capture.Validation?.Checks ?? [])
            .Where(check => check.Outcome == CheckOutcome.Failed)
            .Select(check => check.Description);

        var unsure = LowConfidence(capture.Result?.ReportedConfidence)
            .Concat((capture.Result?.Candidates ?? []).SelectMany(candidate => LowConfidence(candidate.ReportedConfidence)))
            .Distinct()
            .Select(value => $"Extraction was unsure of {value}.");

        return [.. failed, .. unsure];
    }

    /// <summary>The names of the values a run reported low confidence in, in the words a reader knows them by.</summary>
    public static IReadOnlyList<string> LowConfidence(IReadOnlyDictionary<string, decimal>? reported)
    {
        return
        [
            .. (reported ?? new Dictionary<string, decimal>())
                .Where(entry => s_unverifiable.ContainsKey(entry.Key) && entry.Value < ConfidenceThreshold)
                .Select(entry => s_unverifiable[entry.Key]),
        ];
    }

    /// <summary>A candidate as the user first sees it: its numbers as text, its matches as codes.</summary>
    public static EditableLine LineOf(ExtractionCandidateView candidate, IReadOnlyList<CategoryView> categories, IReadOnlyList<UnitView> units)
    {
        var categoryCode = categories.FirstOrDefault(category => category.Id == candidate.CategoryId)?.Code ?? string.Empty;
        var unitCode = units.FirstOrDefault(unit => unit.Id == candidate.UnitId)?.Code ?? string.Empty;

        return new EditableLine(
            candidate.LineNumber,
            candidate.Description,
            Text(candidate.Amount),
            Text(candidate.Quantity),
            categoryCode,
            unitCode,
            candidate.CategoryRaw,
            candidate.UnitRaw,
            candidate.UnitPrice,
            candidate.ListUnitPrice,
            candidate.DiscountAmount);
    }

    public static EditableLine EmptyLine(int key)
    {
        return new EditableLine(key, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null, null, null);
    }

    /// <summary>The date the receipt itself established, where a fiscal code decoded one.</summary>
    public static DateTime? FiscalCreatedAt(CaptureResult capture)
    {
        return capture.Extracted?.CreatedAt ?? capture.Supplied?.CreatedAt;
    }

    public static string DateText(DateTime? value)
    {
        return value?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// The confirmation request: the user's values for everything they can edit, and the capture's own
    /// outcome echoed back exactly as it arrived - never rebuilt from the edits, since the server holds
    /// nothing to correct it against. The date is omitted where the receipt established one the user
    /// left alone. A number that cannot be read is left for the ledger to refuse, in its own words; a
    /// date that cannot be read is the one thing reported here, because sending nothing in its place
    /// would silently let the receipt's date stand in for the one the user typed.
    /// </summary>
    public static ReviewConfirmation Confirmation(CaptureResult capture, ReviewEdits edits)
    {
        DateTime? occurredAt = null;

        if (edits.DateEdited || FiscalCreatedAt(capture) is null)
        {
            if (!string.IsNullOrWhiteSpace(edits.OccurredAt))
            {
                if (!DateTime.TryParseExact(edits.OccurredAt.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    return new ReviewConfirmation(null, DateFormatProblem);
                }

                occurredAt = parsed;
            }
        }

        var expenses = edits.Lines
            .Select(line => new ExpenseRequest(
                line.Description,
                Number(line.Amount) ?? 0m,
                Number(line.Quantity),
                Code(line.UnitCode),
                line.UnitPrice,
                Code(line.CategoryCode),
                line.CategoryRaw,
                line.UnitRaw,
                line.ListUnitPrice,
                line.DiscountAmount))
            .ToList();

        var echo = new CapturedReceiptRequest(
            capture.TempKey,
            capture.State,
            capture.FailureReason,
            capture.Extracted?.Jikr ?? capture.Supplied?.Jikr,
            capture.FiscalSource,
            capture.FiscalPayload);

        var request = new RecordPurchaseRequest(
            Number(edits.Amount) ?? 0m,
            expenses,
            string.IsNullOrWhiteSpace(edits.Merchant) ? null : new MerchantRequest(edits.Merchant.Trim(), null),
            occurredAt,
            echo);

        return new ReviewConfirmation(request, null);
    }

    private static string Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string? Code(string code)
    {
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    /// <summary>A decimal comma is read as a point: either is what a person in the euro area types.</summary>
    private static decimal? Number(string text)
    {
        const NumberStyles Style = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

        return decimal.TryParse(text.Replace(',', '.'), Style, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
