using System.Net;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Navigation;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Navigation;

/// <summary>
/// The hand-offs behind desktop-client's "Confirmation creates a purchase" (home reads the ledger
/// again) and "Upload starts without a further action" (arriving on capture starts it) (D5).
/// </summary>
public sealed class NavigatorTests
{
    private readonly FakeHttpMessageHandler _api = new();
    private readonly MainWindowViewModel _shell = new();
    private readonly FakeImagePicker _picker = new();

    public NavigatorTests()
    {
        _api.Respond("GET", "/purchases", HttpStatusCode.OK, "[]")
            .Respond("GET", "/merchants", HttpStatusCode.OK, "[]")
            .Respond("GET", "/categories", HttpStatusCode.OK, "[]")
            .Respond("GET", "/units", HttpStatusCode.OK, "[]")
            .Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([]));
    }

    [Fact]
    public async Task Going_home_shows_a_home_that_reads_the_ledger()
    {
        Subject().ToHome();

        var home = Assert.IsType<HomeViewModel>(_shell.Current);
        await Eventually(() => !home.IsLoading && _api.To("GET", "/purchases").Any());
    }

    [Fact]
    public async Task Every_return_home_reads_the_ledger_again()
    {
        var navigator = Subject();

        navigator.ToHome();
        var first = _shell.Current;
        await Eventually(() => _api.To("GET", "/purchases").Count() == 1);
        navigator.ToHome();

        Assert.NotSame(first, _shell.Current);
        await Eventually(() => _api.To("GET", "/purchases").Count() == 2);
    }

    [Fact]
    public async Task Arriving_on_capture_starts_the_upload()
    {
        Subject().ToCapture(FakeImagePicker.Image("receipt.jpg"));

        var capture = Assert.IsType<CaptureViewModel>(_shell.Current);
        await Eventually(() => capture.Review is not null);
        Assert.Single(_api.To("POST", "/receipts/capture"));
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private Navigator Subject()
    {
        return new Navigator(_shell, new LedgerClient(FakeHttpMessageHandler.ClientFor(_api)), new FixedClock(new DateTime(2026, 9, 17, 10, 0, 0)), _picker);
    }
}
