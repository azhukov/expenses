using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Rules;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;
using Expenses.Desktop.UI.Tests.Harness;

namespace Expenses.Desktop.E2E.Tests;

/// <summary>
/// The one flow the client exists for, against the real HTTP host and a real PostgreSQL: choose a
/// receipt, correct what extraction could not read, confirm it, and see it in the month. Scenarios from
/// desktop-client: "Confirmation creates a purchase", "Leaving without confirming changes nothing".
///
/// Extraction is deliberately not under test: docker-compose.e2e.yml points the fiscal portal at an
/// address that does not answer, so what comes back is whatever the stack produces without it. The
/// test does what a person does when those lines are not the receipt - reduces them to one and types
/// the figures in. Each run uses its own merchant and amount, so an earlier run's rows cannot satisfy
/// its assertions. Run it through ./test-ui.sh with E2E=1, which brings the stack up.
/// </summary>
public sealed class CaptureToLedgerTests
{
    private static readonly TimeSpan s_extraction = TimeSpan.FromSeconds(45);

    [AvaloniaFact]
    public async Task A_chosen_receipt_becomes_a_purchase_in_the_month()
    {
        using var app = await DesktopHarness.HomeAgainst(Ledger());
        var amount = Math.Round(10m + (decimal)Random.Shared.NextDouble() * 80m, 2);
        var merchant = $"E2E Desktop Merchant {DateTime.Now.Ticks}";
        app.Picker.Next = FakeImagePicker.Image("receipt.jpg", Receipt());

        await app.Click(app.Find<Button>("Capture"));
        await app.Until(() => app.IsShown("Confirm"), "extraction has answered and review is shown", (int)s_extraction.TotalMilliseconds);

        await app.Type(app.Find<TextBox>("Merchant"), merchant);
        await app.Type(app.Find<TextBox>("OccurredAt"), DateTime.Now.ToString(ReviewRules.DateFormat, CultureInfo.InvariantCulture));
        await app.Type(app.Find<TextBox>("Amount"), amount.ToString("0.00", CultureInfo.InvariantCulture));

        // Down to a single line, whatever extraction offered: the ledger rejects a purchase whose amount
        // disagrees with the sum of its expenses.
        while (app.FindAll<Button>("RemoveLine").Count > 1)
        {
            await app.Click(app.FindAll<Button>("RemoveLine")[^1]);
        }

        await app.Type(app.Find<TextBox>("LineDescription"), "Groceries");
        await app.Type(app.Find<TextBox>("LineAmount"), amount.ToString("0.00", CultureInfo.InvariantCulture));
        await app.Type(app.Find<TextBox>("LineQuantity"), "1");

        await app.Click(app.Find<Button>("Confirm"));
        await app.Until(
            () => app.Current is HomeViewModel && app.FindAll<TextBlock>("RowMerchant").Any(row => row.Text == merchant),
            "home shows the new purchase",
            15_000);

        var entry = app.FindAll<TextBlock>("RowMerchant").Single(row => row.Text == merchant).GetVisualAncestors().OfType<Grid>().First();
        var shown = entry.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToList();
        Assert.Contains(shown, text => text.Name == "RowAmount" && text.Text == Formatting.Amount(amount));
        Assert.Contains(shown, text => text.Name == "RowReceipt");
    }

    [AvaloniaFact]
    public async Task Leaving_the_review_without_confirming_changes_nothing()
    {
        using var app = await DesktopHarness.HomeAgainst(Ledger());
        var before = await MonthOf(app.Ledger);
        app.Picker.Next = FakeImagePicker.Image("receipt.jpg", Receipt());

        await app.Click(app.Find<Button>("Capture"));
        await app.Until(() => app.IsShown("Confirm"), "extraction has answered and review is shown", (int)s_extraction.TotalMilliseconds);
        await app.Click(app.Find<Button>("BackFromReview"));
        await app.Until(() => app.Current is HomeViewModel home && !home.IsLoading, "home is shown again", 15_000);

        Assert.Equal(before, await MonthOf(app.Ledger));
    }

    private static Microsoft.Extensions.Configuration.IConfiguration Ledger()
    {
        Assert.False(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LedgerAddress.EnvironmentVariable)),
            $"{LedgerAddress.EnvironmentVariable} is not set. Run this suite through ./test-ui.sh with E2E=1, which brings up the stack it needs.");

        return LedgerAddress.Configuration(AppContext.BaseDirectory);
    }

    private static byte[] Receipt()
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "receipt.jpg"));
    }

    private static async Task<IReadOnlyList<long>> MonthOf(LedgerClient ledger)
    {
        var (from, to) = MonthRules.CurrentMonth(DateTime.Now);

        return [.. (await ledger.ListPurchases(from, to, TestContext.Current.CancellationToken)).Select(purchase => purchase.Id).Order()];
    }
}
