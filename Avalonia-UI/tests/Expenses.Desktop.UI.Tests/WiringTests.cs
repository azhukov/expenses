using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;
using Expenses.Desktop.UI.Tests.Harness;

namespace Expenses.Desktop.UI.Tests;

/// <summary>
/// That the views are bound to the view models at all: what the view-model suite proves is reachable
/// through the controls a person uses. Behaviour is specified there, not here.
/// </summary>
public sealed class WiringTests
{
    [AvaloniaFact]
    public async Task Home_shows_the_capture_action_the_months_total_and_the_recent_purchases()
    {
        using var app = await DesktopHarness.Home(api => api
            .Respond("GET", "/purchases", HttpStatusCode.OK, new[]
            {
                Purchases.Make(2, "2026-09-17T08:00:00", 30.25m, ExtractionState.NeedsReview, merchantRaw: "IDEA 042", lines: 2),
                Purchases.Make(1, "2026-09-02T10:00:00", 7m, merchantRaw: "CAFE BAR PEPE"),
            }));

        Assert.Equal("Capture a receipt", app.Find<Button>("Capture").Content);
        Assert.Equal("€37.25", app.Find<TextBlock>("MonthTotal").Text);
        Assert.Equal("1 receipt needs review", app.Find<TextBlock>("ReviewNotice").Text);

        var merchants = app.FindAll<TextBlock>("RowMerchant").Select(text => text.Text);
        Assert.Equal(["IDEA 042", "CAFE BAR PEPE"], merchants);
        Assert.Equal(["€30.25", "€7.00"], app.FindAll<TextBlock>("RowAmount").Select(text => text.Text));
        Assert.Equal(["Today · 2 lines", "2 Sept 2026 · 0 lines"], app.FindAll<TextBlock>("RowDetail").Select(text => text.Text));
        Assert.Single(app.FindAll<TextBlock>("RowReceipt"));
    }

    [AvaloniaFact]
    public async Task A_failure_to_read_the_ledger_is_shown_with_a_retry_that_works()
    {
        using var app = await DesktopHarness.Home(api => api.Unreachable("GET", "/purchases"));

        Assert.Equal("Purchases could not be loaded. The ledger could not be reached.", app.Find<TextBlock>("LoadFailure").Text);

        app.Api.Respond("GET", "/purchases", HttpStatusCode.OK, "[]");
        await app.Click(app.Find<Button>("Retry"));
        await app.Until(() => app.IsShown("Empty"), "the empty ledger is shown");

        Assert.False(app.IsShown("LoadFailure"));
        Assert.Equal("Nothing recorded yet.", app.Find<TextBlock>("Empty").Text);
    }

    [AvaloniaFact]
    public async Task Clicking_capture_with_a_file_chosen_shows_the_capture_screen_and_its_review()
    {
        using var app = await DesktopHarness.Home();
        app.Api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m)], total: 2.40m, merchantName: "Idea"));
        app.Picker.Next = FakeImagePicker.Image("receipt.jpg");

        await app.Click(app.Find<Button>("Capture"));
        await app.Until(() => app.IsShown("Merchant"), "the review is shown");

        Assert.IsType<CaptureViewModel>(app.Current);
        Assert.Equal("Idea", app.Find<TextBox>("Merchant").Text);
        Assert.Equal("Milk", app.Find<TextBox>("LineDescription").Text);
        Assert.Equal("2.40", app.Find<TextBox>("Amount").Text);
    }

    [AvaloniaFact]
    public async Task Typing_into_the_review_edits_what_will_be_confirmed()
    {
        using var app = await Reviewing(Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m)], total: 2.40m));
        var review = ((CaptureViewModel)app.Current!).Review!;

        await app.Type(app.Find<TextBox>("Merchant"), "Idea Podgorica");
        await app.Type(app.Find<TextBox>("LineDescription"), "Whole milk");
        await app.Type(app.Find<TextBox>("LineQuantity"), "3.");
        await app.Click(app.Find<Button>("AddLine"));

        Assert.Equal("Idea Podgorica", review.Merchant);
        Assert.Equal("Whole milk", review.Lines[0].Description);
        Assert.Equal("3.", review.Lines[0].Quantity);
        Assert.Equal(2, app.FindAll<TextBox>("LineDescription").Count);

        await app.Click(app.FindAll<Button>("RemoveLine")[1]);

        Assert.Single(review.Lines);
    }

    [AvaloniaFact]
    public async Task Confirm_records_the_purchase_and_returns_home()
    {
        using var app = await Reviewing(Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m)], total: 2.40m, fiscalCreatedAt: new DateTime(2026, 9, 17, 8, 0, 0)));
        app.Api.Respond("POST", "/purchases", HttpStatusCode.Created, """
            { "id": 41, "occurredAt": "2026-09-17T08:00:00", "amount": 2.40, "hasReceipt": true, "alreadyRecorded": false }
            """);

        await app.Click(app.Find<Button>("Confirm"));
        await app.Until(() => app.Current is HomeViewModel, "home is shown again");

        Assert.Single(app.Api.To("POST", "/purchases"));
    }

    [AvaloniaFact]
    public async Task Back_from_the_review_is_reachable_from_the_keyboard()
    {
        using var app = await Reviewing(Captures.Extracted([Captures.Candidate(1, "Milk", 2.40m)]));
        var back = app.Find<Button>("BackFromReview");

        back.Focus();
        await app.Press(Key.Enter);
        await app.Until(() => app.Current is HomeViewModel, "home is shown again");

        Assert.Empty(app.Api.To("POST", "/purchases"));
    }

    private static async Task<DesktopHarness> Reviewing(CaptureResult capture)
    {
        var app = await DesktopHarness.Home();
        app.Api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, capture);
        app.Navigator.ToCapture(FakeImagePicker.Image("receipt.jpg"));
        await app.Until(() => app.IsShown("Confirm"), "the review is shown");

        return app;
    }
}
