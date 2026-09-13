using System.Net;
using System.Text;
using System.Text.Json;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Rules;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Screens;

/// <summary>
/// Scenarios from desktop-client: "Confirmation creates a purchase", "Confirming cannot be repeated
/// while outstanding", "The date defaults from the receipt", "A manually entered capture can still be
/// confirmed", "A reconciliation mismatch is reported", "A missing date is reported", "Leaving without
/// confirming changes nothing", "Leaving while extraction is running".
/// </summary>
public sealed class ConfirmAndLeaveTests
{
    private const string Recorded = """
        { "id": 41, "occurredAt": "2026-09-12T18:05:11", "amount": 2.40, "merchantId": null, "merchantRaw": "Idea",
          "hasReceipt": true, "expenses": [], "totalSaving": 0, "savingPercentage": null, "extractionState": "Extracted",
          "alreadyRecorded": false, "merchant": null, "merchantNewlyAdded": false }
        """;

    private readonly FakeHttpMessageHandler _api = new();
    private readonly FakeNavigator _navigator = new();

    public ConfirmAndLeaveTests()
    {
        _api.Respond("GET", "/categories", HttpStatusCode.OK, "[]").Respond("GET", "/units", HttpStatusCode.OK, "[]");
    }

    [Fact]
    public async Task Confirmation_creates_a_purchase_and_returns_home()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.Created, Recorded);
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m)], total: 2.40m, merchantName: "Idea", fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11), tempKey: "tmp-7"));

        await review.ConfirmCommand.ExecuteAsync(null);

        using var body = JsonDocument.Parse(Assert.Single(_api.To("POST", "/purchases")).BodyText);
        Assert.Equal("tmp-7", body.RootElement.GetProperty("capture").GetProperty("tempKey").GetString());
        Assert.Equal("Milk", body.RootElement.GetProperty("expenses")[0].GetProperty("description").GetString());
        Assert.Equal(1, _navigator.HomeCount);
        Assert.Null(review.Rejection);
    }

    [Fact]
    public async Task The_date_defaults_from_the_receipt()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.Created, Recorded);
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m)], total: 2.40m, fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11)));

        await review.ConfirmCommand.ExecuteAsync(null);

        using var body = JsonDocument.Parse(Assert.Single(_api.To("POST", "/purchases")).BodyText);
        Assert.False(body.RootElement.TryGetProperty("occurredAt", out _));
    }

    [Fact]
    public async Task Confirming_cannot_be_repeated_while_outstanding()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Respond("POST", "/purchases", _ => answer.Task);
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m)], total: 2.40m));
        review.OccurredAt = "2026-09-12 18:05";

        var first = review.ConfirmCommand.ExecuteAsync(null);

        Assert.False(review.ConfirmCommand.CanExecute(null));
        Assert.True(review.IsConfirming);
        review.ConfirmCommand.Execute(null);

        // Still outstanding: the ignored second activation must not re-enable the action.
        Assert.False(review.ConfirmCommand.CanExecute(null));
        Assert.True(review.IsConfirming);

        answer.SetResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(Recorded, Encoding.UTF8, "application/json") });
        await first;

        Assert.Single(_api.To("POST", "/purchases"));
        Assert.False(review.IsConfirming);
    }

    [Fact]
    public async Task A_manually_entered_capture_can_still_be_confirmed()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.Created, Recorded);
        var review = await Loaded(Captures.Failed(tempKey: "tmp-by-hand"));

        review.OccurredAt = "2026-09-13 08:30";
        review.Amount = "1.80";
        review.Lines[0].Description = "Coffee";
        review.Lines[0].Amount = "1.80";
        await review.ConfirmCommand.ExecuteAsync(null);

        using var body = JsonDocument.Parse(Assert.Single(_api.To("POST", "/purchases")).BodyText);
        Assert.Equal("tmp-by-hand", body.RootElement.GetProperty("capture").GetProperty("tempKey").GetString());
        Assert.Equal("Failed", body.RootElement.GetProperty("capture").GetProperty("state").GetString());
        Assert.Equal("2026-09-13T08:30:00", body.RootElement.GetProperty("occurredAt").GetString());
        Assert.Equal(1, _navigator.HomeCount);
    }

    [Fact]
    public async Task A_reconciliation_mismatch_is_reported_in_place()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.UnprocessableEntity, """{ "code": "purchase.amount_mismatch", "message": "The expense lines add up to 2.00, not 2.40.", "fields": {}, "correlationId": null }""");
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)], total: 2.40m, fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11)));
        review.Merchant = "Idea";
        review.Lines[0].Description = "Whole milk";
        var before = review.Edits();

        await review.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("The expense lines add up to 2.00, not 2.40.", review.Rejection);
        Assert.Equal(0, _navigator.HomeCount);
        AssertSameEdits(before, review.Edits());
    }

    [Fact]
    public async Task A_missing_date_is_reported_in_place()
    {
        _api.Respond("POST", "/purchases", HttpStatusCode.BadRequest, """{ "code": "purchase.occurred_at_required", "message": "A date is required: the receipt carried no fiscal timestamp.", "fields": {}, "correlationId": null }""");
        var review = await Loaded(Captures.Failed());
        review.Amount = "1";
        review.Lines[0].Amount = "1";
        var before = review.Edits();

        await review.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("A date is required: the receipt carried no fiscal timestamp.", review.Rejection);
        Assert.Equal(0, _navigator.HomeCount);
        AssertSameEdits(before, review.Edits());
    }

    [Fact]
    public async Task A_date_that_cannot_be_read_is_reported_without_sending_anything()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)], fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11)));
        review.OccurredAt = "13/09/2026";

        await review.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(ReviewRules.DateFormatProblem, review.Rejection);
        Assert.Empty(_api.To("POST", "/purchases"));
        Assert.Equal("13/09/2026", review.OccurredAt);
    }

    [Fact]
    public async Task A_rejection_is_cleared_by_the_next_attempt()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)], fiscalCreatedAt: new DateTime(2026, 9, 12, 18, 5, 11)));
        review.OccurredAt = "not a date";
        await review.ConfirmCommand.ExecuteAsync(null);

        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Respond("POST", "/purchases", _ => answer.Task);
        review.OccurredAt = "2026-09-12 18:05";
        var second = review.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(review.Rejection);

        answer.SetResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(Recorded, Encoding.UTF8, "application/json") });
        await second;
    }

    [Fact]
    public async Task Leaving_the_review_without_confirming_changes_nothing()
    {
        var review = await Loaded(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)]));
        var before = _api.Requests.Count;

        review.BackCommand.Execute(null);

        Assert.Equal(1, _navigator.HomeCount);
        Assert.Equal(before, _api.Requests.Count);
        Assert.Empty(_api.To("POST", "/purchases"));
    }

    [Fact]
    public async Task Leaving_the_capture_screen_after_a_failed_upload_changes_nothing()
    {
        _api.Unreachable("POST", "/receipts/capture");
        var capture = new CaptureViewModel(Ledger(), _navigator, FakeImagePicker.Image("receipt.jpg"));
        await capture.StartCommand.ExecuteAsync(null);

        capture.BackCommand.Execute(null);

        Assert.Equal(1, _navigator.HomeCount);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task Leaving_while_extraction_is_running()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        // A transport that ignores cancellation, so what is proved is that the screen discards a late
        // answer, not merely that the request was aborted in time.
        _api.Respond("POST", "/receipts/capture", _ => answer.Task);
        var capture = new CaptureViewModel(Ledger(), _navigator, FakeImagePicker.Image("receipt.jpg"));
        var uploading = capture.StartCommand.ExecuteAsync(null);
        for (var attempt = 0; attempt < 200 && !capture.IsUploading; attempt++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        capture.BackCommand.Execute(null);
        answer.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(Captures.Extracted([Captures.Candidate(1, "Milk", 2m)]), FakeHttpMessageHandler.ApiJson), Encoding.UTF8, "application/json"),
        });
        await uploading;

        Assert.Equal(1, _navigator.HomeCount);
        Assert.Null(capture.Review);
        Assert.Null(capture.UploadFailure);
    }

    private static void AssertSameEdits(ReviewEdits expected, ReviewEdits actual)
    {
        Assert.Equal(expected.Lines, actual.Lines);
        Assert.Equal(expected with { Lines = [] }, actual with { Lines = [] });
    }

    private LedgerClient Ledger()
    {
        return new LedgerClient(FakeHttpMessageHandler.ClientFor(_api));
    }

    private async Task<ReviewViewModel> Loaded(CaptureResult capture)
    {
        var review = new ReviewViewModel(Ledger(), _navigator, capture);
        await review.Load(TestContext.Current.CancellationToken);

        return review;
    }
}
