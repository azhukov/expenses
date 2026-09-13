using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Screens;

/// <summary>
/// Scenarios from desktop-client: "Choosing a file captures it", "Capture is abandoned", "Dropping a
/// file captures it", "Several files are dropped at once", "Something other than a file is dropped",
/// "Home does not submit the image".
/// </summary>
public sealed class HomeCaptureTests
{
    private readonly FakeHttpMessageHandler _api = new();
    private readonly FakeImagePicker _picker = new();
    private readonly FakeNavigator _navigator = new();

    [Fact]
    public async Task Choosing_a_file_captures_it()
    {
        var chosen = FakeImagePicker.Image("receipt.jpg");
        _picker.Next = chosen;
        var home = Subject();

        await home.CaptureCommand.ExecuteAsync(null);

        Assert.Equal(1, _picker.Opened);
        Assert.Same(chosen, Assert.Single(_navigator.Captures));
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task Capture_is_abandoned()
    {
        _picker.Next = null;
        var home = Subject();

        await home.CaptureCommand.ExecuteAsync(null);

        Assert.Equal(1, _picker.Opened);
        Assert.Empty(_navigator.Captures);
        Assert.Null(home.CaptureNotice);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public void Dropping_a_file_captures_it()
    {
        var dropped = FakeImagePicker.Image("scan.png");
        var home = Subject();

        home.AcceptDrop([dropped]);

        Assert.Same(dropped, Assert.Single(_navigator.Captures));
        Assert.Equal(0, _picker.Opened);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public void Several_files_are_dropped_at_once()
    {
        var home = Subject();

        home.AcceptDrop([FakeImagePicker.Image("one.jpg"), FakeImagePicker.Image("two.jpg")]);

        Assert.Empty(_navigator.Captures);
        Assert.Equal("Receipts are captured one at a time. Drop a single file.", home.CaptureNotice);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task A_later_single_capture_clears_the_one_at_a_time_notice()
    {
        var home = Subject();
        home.AcceptDrop([FakeImagePicker.Image("one.jpg"), FakeImagePicker.Image("two.jpg")]);
        _picker.Next = FakeImagePicker.Image("one.jpg");

        await home.CaptureCommand.ExecuteAsync(null);

        Assert.Null(home.CaptureNotice);
    }

    [Fact]
    public void Something_other_than_a_file_is_dropped()
    {
        var home = Subject();

        home.AcceptDrop([]);

        Assert.Empty(_navigator.Captures);
        Assert.Null(home.CaptureNotice);
        Assert.Empty(_api.Requests);
    }

    private HomeViewModel Subject()
    {
        return new HomeViewModel(
            new LedgerClient(FakeHttpMessageHandler.ClientFor(_api)),
            new FixedClock(new DateTime(2026, 9, 17, 10, 0, 0)),
            _picker,
            _navigator);
    }
}
