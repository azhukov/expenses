using System.Net;
using System.Text;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Navigation;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Screens;

/// <summary>
/// Scenarios from desktop-client: "Upload starts without a further action", "One capture is one
/// upload", "The wait is visible", "The upload cannot be reached", "An unusual format is not
/// pre-judged", "Original bytes are preserved".
/// </summary>
public sealed class CaptureViewModelTests
{
    private readonly FakeHttpMessageHandler _api = new();
    private readonly FakeNavigator _navigator = new();

    public CaptureViewModelTests()
    {
        _api.Respond("GET", "/categories", HttpStatusCode.OK, "[]").Respond("GET", "/units", HttpStatusCode.OK, "[]");
    }

    [Fact]
    public async Task Upload_starts_without_a_further_action()
    {
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([Captures.Candidate(1, "Milk", 2m)]));
        var capture = Subject(FakeImagePicker.Image("receipt.jpg", [0xFF, 0xD8, 0x01]));

        await capture.StartCommand.ExecuteAsync(null);

        var upload = Assert.Single(_api.To("POST", "/receipts/capture"));
        Assert.Contains("receipt.jpg", upload.BodyText, StringComparison.Ordinal);
        Assert.NotNull(capture.Review);
        Assert.Null(capture.UploadFailure);
    }

    [Fact]
    public async Task One_capture_is_one_upload()
    {
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([]));
        var capture = Subject(FakeImagePicker.Image("receipt.jpg"));

        await capture.StartCommand.ExecuteAsync(null);
        await capture.StartCommand.ExecuteAsync(null);

        Assert.Single(_api.To("POST", "/receipts/capture"));
    }

    [Fact]
    public async Task The_wait_is_visible()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Respond("POST", "/receipts/capture", _ => answer.Task);
        var capture = Subject(FakeImagePicker.Image("receipt.jpg"));

        var uploading = capture.StartCommand.ExecuteAsync(null);
        await Eventually(() => capture.IsUploading);

        Assert.Null(capture.Review);
        Assert.Null(capture.UploadFailure);

        answer.SetResult(Json(Captures.Extracted([])));
        await uploading;

        Assert.False(capture.IsUploading);
        Assert.NotNull(capture.Review);
    }

    [Fact]
    public async Task The_upload_cannot_be_reached_and_a_retry_sends_the_bytes_originally_chosen()
    {
        _api.Unreachable("POST", "/receipts/capture");

        // A file that changes on disk after it was chosen: each opening returns different bytes.
        var openings = 0;
        var image = new ReceiptImage("receipt.jpg", _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes($"version {++openings}"))));
        var capture = Subject(image);

        await capture.StartCommand.ExecuteAsync(null);

        Assert.Equal("The receipt could not be uploaded. The ledger could not be reached.", capture.UploadFailure);
        Assert.Null(capture.Review);
        Assert.True(capture.RetryCommand.CanExecute(null));

        _api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([]));
        await capture.RetryCommand.ExecuteAsync(null);

        var uploads = _api.To("POST", "/receipts/capture").ToList();
        Assert.Equal(2, uploads.Count);
        Assert.Contains("version 1", uploads[1].BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("version 2", uploads[1].BodyText, StringComparison.Ordinal);
        Assert.Null(capture.UploadFailure);
        Assert.NotNull(capture.Review);
    }

    [Fact]
    public async Task The_ledgers_judgement_of_a_file_is_shown()
    {
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.BadRequest, """{ "code": "receipt.image.unsupported_format", "message": "That file is not an image the ledger can read.", "fields": {}, "correlationId": null }""");
        var capture = Subject(FakeImagePicker.Image("notes.txt", "plain text"));

        await capture.StartCommand.ExecuteAsync(null);

        Assert.Equal("The receipt could not be uploaded. That file is not an image the ledger can read.", capture.UploadFailure);
    }

    [Fact]
    public async Task An_unusual_format_is_not_pre_judged()
    {
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([]));
        var capture = Subject(FakeImagePicker.Image("receipt.heic", "ftypheic not displayable here"));

        await capture.StartCommand.ExecuteAsync(null);

        Assert.Single(_api.To("POST", "/receipts/capture"));
        Assert.Null(capture.UploadFailure);
    }

    private static HttpResponseMessage Json(object body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body, FakeHttpMessageHandler.ApiJson), Encoding.UTF8, "application/json"),
        };
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private CaptureViewModel Subject(ReceiptImage image)
    {
        return new CaptureViewModel(new LedgerClient(FakeHttpMessageHandler.ClientFor(_api)), _navigator, image);
    }
}
