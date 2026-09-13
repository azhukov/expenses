using System.Net;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Rules;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Screens;

/// <summary>
/// Scenarios from desktop-client: "Candidates are shown for review", "Candidates are editable", "A
/// partly typed number is not lost", "A low-confidence value is flagged", "An arithmetic mismatch is
/// named", "An authoritative result that does not reconcile is still shown as read", "Failure is
/// explained".
/// </summary>
public sealed class ReviewViewModelTests
{
    private static readonly CategoryView[] s_categories = [new(3, "groceries", "Groceries", null, null, true, true)];

    private static readonly UnitView[] s_units = [new(2, "kg", "Kilogram", "kg", UnitKind.Mass, true)];

    private readonly FakeHttpMessageHandler _api = new();
    private readonly FakeNavigator _navigator = new();

    public ReviewViewModelTests()
    {
        _api.Respond("GET", "/categories", HttpStatusCode.OK, s_categories).Respond("GET", "/units", HttpStatusCode.OK, s_units);
    }

    [Theory]
    [InlineData(ExtractionState.Extracted)]
    [InlineData(ExtractionState.NeedsReview)]
    public async Task Candidates_are_shown_for_review(ExtractionState state)
    {
        var review = await Loaded(Captures.Extracted(
            [Captures.Candidate(1, "Milk", 2.40m, quantity: 2m, categoryId: 3), Captures.Candidate(2, "Apples", 3.10m, quantity: 1.250m, unitId: 2)],
            total: 5.50m,
            merchantName: "Idea",
            state: state,
            fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11)));

        Assert.Equal("Idea", review.Merchant);
        Assert.Equal("5.50", review.Amount);
        Assert.Equal("2026-09-12 18:05", review.OccurredAt);
        Assert.Equal(["Milk", "Apples"], review.Lines.Select(line => line.Description));
        Assert.Equal(["2.40", "3.10"], review.Lines.Select(line => line.Amount));
        Assert.Equal(["2", "1.250"], review.Lines.Select(line => line.Quantity));
        Assert.Equal(["groceries", string.Empty], review.Lines.Select(line => line.CategoryCode));
        Assert.Equal([string.Empty, "kg"], review.Lines.Select(line => line.UnitCode));
        Assert.Equal(s_categories, review.Categories);
        Assert.Equal(s_units, review.Units);
        Assert.Null(review.FailureReason);
    }

    [Fact]
    public async Task Candidates_are_editable()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m, categoryRaw: "DAIRY"), Captures.Candidate(2, "Bread", 1m)], total: 3.40m));

        var milk = review.Lines[0];
        milk.Description = "Whole milk";
        milk.Amount = "2.60";
        milk.Quantity = "2";
        milk.CategoryCode = "groceries";
        milk.UnitCode = "kg";
        review.RemoveLineCommand.Execute(review.Lines[1]);
        review.AddLineCommand.Execute(null);

        var edits = review.Edits();
        Assert.Equal(2, edits.Lines.Count);
        Assert.Equal(new EditableLine(1, "Whole milk", "2.60", "2", "groceries", "kg", "DAIRY", null, null, null, null), edits.Lines[0]);
        Assert.Equal(ReviewRules.EmptyLine(edits.Lines[1].Key), edits.Lines[1]);
        Assert.NotEqual(1, edits.Lines[1].Key);
        Assert.NotEqual(2, edits.Lines[1].Key);
    }

    [Fact]
    public async Task A_category_or_unit_can_be_cleared_as_well_as_chosen()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m, categoryId: 3)]));

        Assert.Equal([new Choice(string.Empty, "Uncategorised"), new Choice("groceries", "Groceries")], review.CategoryChoices);
        Assert.Equal([new Choice(string.Empty, "None"), new Choice("kg", "Kilogram")], review.UnitChoices);

        review.Lines[0].CategoryCode = string.Empty;

        Assert.Null(ReviewRules.Confirmation(review.Capture, review.Edits() with { OccurredAt = "2026-09-13 10:00", DateEdited = true }).Request!.Expenses[0].CategoryCode);
    }

    [Fact]
    public async Task Added_lines_never_share_a_key()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)]));

        review.AddLineCommand.Execute(null);
        review.RemoveLineCommand.Execute(review.Lines[^1]);
        review.AddLineCommand.Execute(null);
        review.AddLineCommand.Execute(null);

        Assert.Equal(review.Lines.Count, review.Lines.Select(line => line.Key).Distinct().Count());
    }

    [Fact]
    public async Task A_partly_typed_number_is_not_lost()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)]));

        review.Amount = "3.";
        review.Lines[0].Quantity = "0,";

        Assert.Equal("3.", review.Amount);
        Assert.Equal("0,", review.Lines[0].Quantity);
        Assert.Equal("3.", review.Edits().Amount);
    }

    [Fact]
    public async Task The_date_is_the_receipts_until_the_user_changes_it()
    {
        var review = await Loaded(Captures.Extracted([], fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11)));

        Assert.False(review.Edits().DateEdited);

        review.OccurredAt = "2026-09-13 09:00";

        Assert.True(review.Edits().DateEdited);
        Assert.Equal("2026-09-13 09:00", review.Edits().OccurredAt);
    }

    [Fact]
    public async Task A_low_confidence_value_is_flagged_on_its_line()
    {
        var review = await Loaded(Captures.Extracted(
            [Captures.Candidate(1, "Mlk", 2m, confidence: new Dictionary<string, decimal> { ["description"] = 0.3m }), Captures.Candidate(2, "Bread", 1m)],
            state: ExtractionState.NeedsReview));

        Assert.Equal("Extraction was unsure of the description.", review.Lines[0].Unsure);
        Assert.Null(review.Lines[1].Unsure);
        Assert.Contains("Extraction was unsure of the description.", review.Reasons);
    }

    [Fact]
    public async Task An_authoritative_result_that_does_not_reconcile_is_still_shown_as_read()
    {
        var review = await Loaded(Captures.Extracted(
            [Captures.Candidate(1, "Milk", 2m), Captures.Candidate(2, "Bread", 1m)],
            total: 3.50m,
            state: ExtractionState.NeedsReview,
            checks: [Captures.Check("The lines add up to 3.00, not the total of 3.50.", CheckOutcome.Failed)],
            fiscalSource: FiscalSource.RetrievedFromService));

        Assert.Equal(["The lines add up to 3.00, not the total of 3.50."], review.Reasons);
        Assert.Equal(["2", "1"], review.Lines.Select(line => line.Amount));
        Assert.Equal("3.50", review.Amount);
    }

    [Fact]
    public async Task Failure_is_explained()
    {
        var review = await Loaded(Captures.Failed("No text could be read from the image."));

        Assert.Equal("No text could be read from the image.", review.FailureReason);
        var line = Assert.Single(review.Lines);
        Assert.Equal(string.Empty, line.Description);
        Assert.Equal(string.Empty, line.Amount);
        Assert.Equal(string.Empty, review.Amount);
        Assert.Equal(string.Empty, review.OccurredAt);
        Assert.Equal(string.Empty, review.Merchant);
    }

    [Fact]
    public async Task Reference_data_that_cannot_be_read_still_leaves_the_capture_reviewable()
    {
        _api.Unreachable("GET", "/categories").Unreachable("GET", "/units");

        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Apples", 3m, categoryId: 3, categoryRaw: "FRUTA")]));

        Assert.Empty(review.Categories);
        Assert.Equal("Apples", Assert.Single(review.Lines).Description);
        Assert.Equal("FRUTA", review.Edits().Lines[0].CategoryRaw);
    }

    [Fact]
    public async Task Loading_twice_does_not_reseed_over_the_users_edits()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)]));
        review.Lines[0].Description = "Oat milk";

        await review.Load(TestContext.Current.CancellationToken);

        Assert.Equal("Oat milk", Assert.Single(review.Lines).Description);
    }

    [Fact]
    public async Task The_capture_screen_hands_its_result_to_a_loaded_review()
    {
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([Captures.Candidate(1, "Milk", 2m)], merchantName: "Idea"));
        var capture = new CaptureViewModel(new LedgerClient(FakeHttpMessageHandler.ClientFor(_api)), _navigator, FakeImagePicker.Image("receipt.jpg"));

        await capture.StartCommand.ExecuteAsync(null);

        Assert.Equal("Idea", capture.Review!.Merchant);
        Assert.Equal("Milk", Assert.Single(capture.Review.Lines).Description);
    }

    private async Task<ReviewViewModel> Loaded(CaptureResult capture)
    {
        var review = new ReviewViewModel(new LedgerClient(FakeHttpMessageHandler.ClientFor(_api)), _navigator, capture);
        await review.Load(TestContext.Current.CancellationToken);

        return review;
    }
}
