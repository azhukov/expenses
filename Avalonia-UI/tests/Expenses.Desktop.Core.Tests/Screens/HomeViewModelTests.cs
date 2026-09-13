using System.Net;
using System.Text.RegularExpressions;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Screens;

/// <summary>
/// Scenarios from desktop-client: "Loading is visible", "Recent purchases are shown", "A purchase
/// carrying a receipt is distinguishable", "An empty ledger", "An empty ledger is not a failure", "No
/// purchases this month", "Purchases needing review exist", "Nothing needs review", "The ledger cannot
/// be reached", "Retrying succeeds", "The ledger reports a specific error", "Reference data cannot be
/// read", "Identifiers are never displayed", "Capture survives a failure to read the ledger".
/// </summary>
public sealed class HomeViewModelTests
{
    private static readonly MerchantView[] s_merchants = [new(7, "Mercadona", null, null, null, true)];

    private readonly FakeHttpMessageHandler _api = new();
    private readonly FakeImagePicker _picker = new();
    private readonly FakeNavigator _navigator = new();

    [Fact]
    public async Task Loading_is_visible()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Respond("GET", "/purchases", _ => answer.Task).Respond("GET", "/merchants", HttpStatusCode.OK, s_merchants);
        var home = Subject();

        var loading = home.LoadCommand.ExecuteAsync(null);

        Assert.True(home.IsLoading);
        Assert.False(home.IsEmpty);
        Assert.Null(home.LoadFailure);

        answer.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        await loading;

        Assert.False(home.IsLoading);
    }

    [Fact]
    public async Task The_current_month_is_what_is_read()
    {
        Month([]);

        await Loaded();

        Assert.Equal("?from=2026-09-01&to=2026-09-30", Assert.Single(_api.To("GET", "/purchases")).Uri.Query);
    }

    [Fact]
    public async Task Recent_purchases_are_shown()
    {
        Month(
            Purchases.Make(1, "2026-09-02T10:00:00", 7m),
            Purchases.Make(3, "2026-09-17T08:00:00", 30.25m, ExtractionState.Extracted, merchantId: 7, merchantRaw: "MERCADONA S.A.", lines: 2),
            Purchases.Make(2, "2026-09-16T21:00:00", 12.5m, merchantRaw: "CAFE BAR PEPE", lines: 1));

        var home = await Loaded();

        Assert.Equal(
            [
                new RecentPurchaseRow("Mercadona", "Today", "2 lines", true, "€30.25"),
                new RecentPurchaseRow("CAFE BAR PEPE", "Yesterday", "1 line", false, "€12.50"),
                new RecentPurchaseRow("Unknown merchant", "2 Sept 2026", "0 lines", false, "€7.00"),
            ],
            home.Recent);
        Assert.False(home.IsEmpty);
    }

    [Fact]
    public async Task A_purchase_carrying_a_receipt_is_distinguishable()
    {
        Month(Purchases.Make(1, "2026-09-10T10:00:00", 1m, ExtractionState.Extracted), Purchases.Make(2, "2026-09-11T10:00:00", 1m));

        var home = await Loaded();

        Assert.Equal([false, true], home.Recent.Select(row => row.HasReceipt));
    }

    [Fact]
    public async Task An_empty_ledger_is_not_a_failure()
    {
        Month([]);

        var home = await Loaded();

        Assert.True(home.IsEmpty);
        Assert.Null(home.LoadFailure);
        Assert.Empty(home.Recent);
        Assert.True(home.CaptureCommand.CanExecute(null));
    }

    [Fact]
    public async Task No_purchases_this_month_totals_zero()
    {
        Month([]);

        Assert.Equal("€0.00", (await Loaded()).MonthTotal);
    }

    [Fact]
    public async Task The_total_reflects_the_current_month()
    {
        Month(Purchases.Make(1, "2026-09-16T19:30:00", 12.5m), Purchases.Make(2, "2026-09-01T00:15:00", 7m), Purchases.Make(3, "2026-08-31T23:45:00", 100m));

        Assert.Equal("€19.50", (await Loaded()).MonthTotal);
    }

    [Theory]
    [InlineData(1, "1 receipt needs review")]
    [InlineData(2, "2 receipts need review")]
    public async Task Purchases_needing_review_exist(int count, string notice)
    {
        Month([.. Enumerable.Range(1, count).Select(each => Purchases.Make(each, "2026-09-10T10:00:00", 1m, ExtractionState.NeedsReview))]);

        Assert.Equal(notice, (await Loaded()).ReviewNotice);
    }

    [Fact]
    public async Task Nothing_needs_review()
    {
        Month(Purchases.Make(1, "2026-09-10T10:00:00", 1m, ExtractionState.Extracted));

        Assert.Null((await Loaded()).ReviewNotice);
    }

    [Fact]
    public async Task The_ledger_cannot_be_reached()
    {
        _api.Unreachable("GET", "/purchases").Respond("GET", "/merchants", HttpStatusCode.OK, s_merchants);

        var home = await Loaded();

        Assert.Equal("Purchases could not be loaded. The ledger could not be reached.", home.LoadFailure);
        Assert.False(home.IsLoading);
        Assert.False(home.IsEmpty);
        Assert.True(home.RetryCommand.CanExecute(null));
        Assert.True(home.CaptureCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_ledger_reports_a_specific_error()
    {
        _api.Respond("GET", "/purchases", HttpStatusCode.ServiceUnavailable, """{ "code": "db.unavailable", "message": "The database is starting up.", "fields": {}, "correlationId": null }""")
            .Respond("GET", "/merchants", HttpStatusCode.OK, s_merchants);

        Assert.Equal("Purchases could not be loaded. The database is starting up.", (await Loaded()).LoadFailure);
    }

    [Fact]
    public async Task Retrying_succeeds()
    {
        _api.Unreachable("GET", "/purchases").Respond("GET", "/merchants", HttpStatusCode.OK, s_merchants);
        var home = await Loaded();

        Month(Purchases.Make(1, "2026-09-10T10:00:00", 4m, merchantId: 7));
        await home.RetryCommand.ExecuteAsync(null);

        Assert.Null(home.LoadFailure);
        Assert.Equal("Mercadona", Assert.Single(home.Recent).Merchant);
        Assert.Equal("€4.00", home.MonthTotal);
    }

    [Fact]
    public async Task Reference_data_cannot_be_read()
    {
        _api.Respond("GET", "/purchases", HttpStatusCode.OK, new[] { Purchases.Make(1, "2026-09-10T10:00:00", 4m, merchantId: 7, merchantRaw: "MERCADONA S.A.") })
            .Unreachable("GET", "/merchants");

        var home = await Loaded();

        Assert.Null(home.LoadFailure);
        Assert.Equal("MERCADONA S.A.", Assert.Single(home.Recent).Merchant);
    }

    [Fact]
    public async Task Identifiers_are_never_displayed()
    {
        Month(
            Purchases.Make(4101, "2026-09-10T10:00:00", 4m, merchantId: 7, merchantRaw: "MERCADONA S.A."),
            Purchases.Make(4102, "2026-09-11T10:00:00", 4m, merchantId: 99));

        var home = await Loaded();

        Assert.All(
            home.Recent.SelectMany(row => new[] { row.Merchant, row.When, row.Lines, row.Amount }),
            text => Assert.DoesNotMatch(new Regex(@"\b(7|99|4101|4102)\b"), text));
    }

    private HomeViewModel Subject()
    {
        return new HomeViewModel(
            new LedgerClient(FakeHttpMessageHandler.ClientFor(_api)),
            new FixedClock(new DateTime(2026, 9, 17, 10, 0, 0)),
            _picker,
            _navigator);
    }

    private void Month(params PurchaseView[] purchases)
    {
        _api.Respond("GET", "/purchases", HttpStatusCode.OK, purchases).Respond("GET", "/merchants", HttpStatusCode.OK, s_merchants);
    }

    private async Task<HomeViewModel> Loaded()
    {
        var home = Subject();
        await home.LoadCommand.ExecuteAsync(null);

        return home;
    }
}
