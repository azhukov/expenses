using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Rules;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Rules;

/// <summary>
/// Ported from FE/src/capture/review.ts and the review cases in FE/src/routes/Capture.test.tsx (D7).
/// Scenarios from desktop-client: "An arithmetic mismatch is named", "A low-confidence value is
/// flagged", "Candidates are shown for review", "Failure is explained", "The date defaults from the
/// receipt", "The capture result is not re-requested", "A manually entered capture can still be
/// confirmed", "A partly typed number is not lost".
/// </summary>
public sealed class ReviewRulesTests
{
    private static readonly CategoryView[] s_categories = [new(3, "groceries", "Groceries", null, null, true, true)];

    private static readonly UnitView[] s_units = [new(2, "kg", "Kilogram", "kg", UnitKind.Mass, true)];

    [Fact]
    public void An_arithmetic_mismatch_is_named_in_the_responses_own_words()
    {
        var capture = Captures.Extracted(
            [Captures.Candidate(1, "Milk", 2m)],
            state: ExtractionState.NeedsReview,
            checks:
            [
                Captures.Check("The lines add up to 2.00, not the total of 2.40.", CheckOutcome.Failed),
                Captures.Check("The tax reconciles.", CheckOutcome.Passed),
                Captures.Check("No discount to check.", CheckOutcome.NotApplicable),
            ]);

        Assert.Equal(["The lines add up to 2.00, not the total of 2.40."], ReviewRules.Reasons(capture));
    }

    [Fact]
    public void A_low_confidence_value_is_named_once_however_many_lines_report_it()
    {
        var unsure = new Dictionary<string, decimal> { ["description"] = 0.4m, ["amount"] = 0.1m };
        var capture = Captures.Extracted(
            [Captures.Candidate(1, "Mlk", 2m, confidence: unsure), Captures.Candidate(2, "Brd", 1m, confidence: unsure)],
            state: ExtractionState.NeedsReview,
            confidence: new Dictionary<string, decimal> { ["merchant_name"] = 0.5m });

        Assert.Equal(
            ["Extraction was unsure of the merchant name.", "Extraction was unsure of the description."],
            ReviewRules.Reasons(capture));
    }

    [Theory]
    [InlineData("description", "0.69", "the description")]
    [InlineData("merchant_name", "0.1", "the merchant name")]
    [InlineData("category_guess", "0.2", "the category")]
    [InlineData("unit_guess", "0.3", "the unit")]
    public void Only_values_no_arithmetic_can_decide_are_flagged_below_the_threshold(string key, string confidence, string named)
    {
        var reported = new Dictionary<string, decimal> { [key] = decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture) };

        Assert.Equal([named], ReviewRules.LowConfidence(reported));
    }

    [Fact]
    public void A_value_at_the_threshold_or_a_numeric_one_is_not_flagged()
    {
        Assert.Empty(ReviewRules.LowConfidence(new Dictionary<string, decimal> { ["description"] = 0.7m, ["amount"] = 0.01m }));
        Assert.Empty(ReviewRules.LowConfidence(null));
    }

    [Fact]
    public void A_candidate_is_seeded_as_editable_text_with_its_matches_resolved_to_codes()
    {
        var line = ReviewRules.LineOf(
            Captures.Candidate(4, "Apples", 3.10m, quantity: 1.250m, categoryId: 3, unitId: 2, categoryRaw: "FRUTA", unitRaw: "KG"),
            s_categories,
            s_units);

        Assert.Equal(new EditableLine(4, "Apples", "3.10", "1.250", "groceries", "kg", "FRUTA", "KG", null, null, null), line);
    }

    [Fact]
    public void A_match_the_dictionary_does_not_describe_leaves_the_code_empty_and_the_raw_text_kept()
    {
        var line = ReviewRules.LineOf(Captures.Candidate(1, "Apples", 3m, categoryId: 99, categoryRaw: "FRUTA"), s_categories, []);

        Assert.Equal(string.Empty, line.CategoryCode);
        Assert.Equal("FRUTA", line.CategoryRaw);
        Assert.Equal(string.Empty, line.Quantity);
    }

    [Fact]
    public void Failure_is_explained_with_empty_fields_offered()
    {
        var line = ReviewRules.EmptyLine(1);

        Assert.Equal(new EditableLine(1, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null, null, null), line);
    }

    [Fact]
    public void The_receipts_own_date_is_the_extracted_fiscal_timestamp_else_the_supplied_one()
    {
        var created = new DateTime(2026, 9, 12, 18, 5, 11);
        var supplied = Captures.Extracted([]) with { Supplied = new FiscalIdentifiers(null, null, null, created, null) };

        Assert.Equal(created, ReviewRules.FiscalCreatedAt(Captures.Extracted([], fiscalCreatedAt: created)));
        Assert.Equal(created, ReviewRules.FiscalCreatedAt(supplied));
        Assert.Null(ReviewRules.FiscalCreatedAt(Captures.Failed()));
    }

    [Fact]
    public void A_date_is_shown_to_the_minute_and_read_back_as_written()
    {
        Assert.Equal("2026-09-12 18:05", ReviewRules.DateText(new DateTime(2026, 9, 12, 18, 5, 11)));
        Assert.Equal(string.Empty, ReviewRules.DateText(null));
    }

    [Fact]
    public void The_capture_result_is_echoed_not_re_derived_from_the_edits()
    {
        var capture = Captures.Extracted(
            [Captures.Candidate(1, "Milk", 2.40m)],
            total: 2.40m,
            state: ExtractionState.NeedsReview,
            fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11),
            fiscalSource: FiscalSource.DecodedFromCode,
            fiscalPayload: "https://efi.example/verify",
            tempKey: "tmp-original");
        var edits = new ReviewEdits(
            [new EditableLine(1, "Whole milk", "3.00", "2", "groceries", string.Empty, "DAIRY", null, 1.5m, null, null)],
            "3.00",
            "Idea Podgorica",
            "2026-09-13 09:00",
            DateEdited: true);

        var request = ReviewRules.Confirmation(capture, edits).Request!;

        Assert.Equal(new CapturedReceiptRequest("tmp-original", ExtractionState.NeedsReview, null, "JIKR-1", FiscalSource.DecodedFromCode, "https://efi.example/verify"), request.Capture);
        Assert.Equal(3.00m, request.Amount);
        Assert.Equal(new MerchantRequest("Idea Podgorica", null), request.Merchant);
        Assert.Equal(new DateTime(2026, 9, 13, 9, 0, 0), request.OccurredAt);
        Assert.Equal(new ExpenseRequest("Whole milk", 3.00m, 2m, null, 1.5m, "groceries", "DAIRY", null, null, null), Assert.Single(request.Expenses));
    }

    [Fact]
    public void The_date_defaults_from_the_receipt_when_the_user_left_it_alone()
    {
        var capture = Captures.Extracted([], fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11));

        var result = ReviewRules.Confirmation(capture, new ReviewEdits([], "0", string.Empty, "2026-09-12 18:05", DateEdited: false));

        Assert.Null(result.Problem);
        Assert.Null(result.Request!.OccurredAt);
    }

    [Fact]
    public void A_manually_entered_capture_is_confirmed_the_same_way()
    {
        var edits = new ReviewEdits([new EditableLine(1, "Coffee", "1,80", string.Empty, string.Empty, string.Empty, null, null, null, null, null)], "1.80", "  ", "2026-09-13 08:30", DateEdited: true);

        var request = ReviewRules.Confirmation(Captures.Failed(tempKey: "tmp-failed"), edits).Request!;

        Assert.Equal("tmp-failed", request.Capture!.TempKey);
        Assert.Equal(ExtractionState.Failed, request.Capture.State);
        Assert.Equal("No text could be read from the image.", request.Capture.FailureReason);
        Assert.Null(request.Merchant);
        Assert.Equal(new DateTime(2026, 9, 13, 8, 30, 0), request.OccurredAt);

        var line = Assert.Single(request.Expenses);
        Assert.Equal(1.80m, line.Amount);
        Assert.Null(line.Quantity);
        Assert.Null(line.CategoryCode);
        Assert.Null(line.UnitCode);
    }

    [Fact]
    public void A_blank_date_with_nothing_from_the_receipt_is_left_for_the_ledger_to_refuse()
    {
        var result = ReviewRules.Confirmation(Captures.Failed(), new ReviewEdits([], "1", string.Empty, "   ", DateEdited: false));

        Assert.Null(result.Problem);
        Assert.Null(result.Request!.OccurredAt);
    }

    [Fact]
    public void A_date_that_cannot_be_read_is_reported_rather_than_replaced_by_the_receipts()
    {
        var capture = Captures.Extracted([], fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11));

        var result = ReviewRules.Confirmation(capture, new ReviewEdits([], "1", string.Empty, "yesterday-ish", DateEdited: true));

        Assert.Null(result.Request);
        Assert.Equal(ReviewRules.DateFormatProblem, result.Problem);
    }

    [Fact]
    public void A_number_that_cannot_be_read_is_left_for_the_ledger_to_refuse()
    {
        var edits = new ReviewEdits([new EditableLine(1, "Coffee", "3.", "abc", string.Empty, string.Empty, null, null, null, null, null)], "x", string.Empty, "2026-09-13 08:30", DateEdited: true);

        var request = ReviewRules.Confirmation(Captures.Failed(), edits).Request!;

        Assert.Equal(0m, request.Amount);
        Assert.Equal(3m, Assert.Single(request.Expenses).Amount);
        Assert.Null(request.Expenses[0].Quantity);
    }
}
