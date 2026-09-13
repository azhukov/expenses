using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;
using Expenses.Desktop.UI.Tests.Harness;

namespace Expenses.Desktop.UI.Tests;

/// <summary>
/// Scenarios from desktop-client: "Capture is reachable on arrival", "Capture stays reachable while
/// browsing recent purchases", "Capture survives a failure to read the ledger", "No breakdown
/// accompanies the total".
/// </summary>
public sealed class HomeLayoutTests
{
    [AvaloniaFact]
    public async Task Capture_is_reachable_on_arrival_and_is_the_most_prominent_control()
    {
        using var app = await DesktopHarness.Home(api => api.Unreachable("GET", "/purchases"));
        var capture = app.Find<Button>("Capture");

        AssertWithinWindow(app, capture);

        var others = app.Window.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control is Button or TextBox or ComboBox && control != capture && control.IsEffectivelyVisible);
        // Taller as well as larger: a longer label alone must not make a button of ordinary size the
        // prominent one.
        Assert.All(others, other => Assert.True(
            Area(capture) > Area(other) && capture.Bounds.Height > other.Bounds.Height,
            $"'{other.Name}' ({other.Bounds.Size}) is as prominent as capture ({capture.Bounds.Size})."));
    }

    [AvaloniaFact]
    public async Task Capture_stays_reachable_while_browsing_recent_purchases()
    {
        using var app = await DesktopHarness.Home(
            api => api.Respond("GET", "/purchases", HttpStatusCode.OK, Enumerable.Range(1, 8).Select(day => Purchases.Make(day, $"2026-09-{day:00}T10:00:00", day, merchantRaw: $"Shop {day}")).ToArray()),
            height: 420);
        var scroll = app.Find<ScrollViewer>("RecentScroll");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the list must be long enough to scroll for this to prove anything");

        scroll.Offset = new Vector(0, scroll.Extent.Height);
        await app.Settle();

        Assert.True(scroll.Offset.Y > 0);
        var capture = app.Find<Button>("Capture");
        AssertWithinWindow(app, capture);

        app.Picker.Next = null;
        await app.Click(capture);
        Assert.Equal(1, app.Picker.Opened);
    }

    [AvaloniaFact]
    public async Task Capture_survives_a_failure_to_read_the_ledger()
    {
        using var app = await DesktopHarness.Home(api => api.Unreachable("GET", "/purchases").Unreachable("GET", "/merchants"));
        app.Api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([]));
        app.Picker.Next = FakeImagePicker.Image("receipt.jpg");

        Assert.True(app.IsShown("LoadFailure"));
        await app.Click(app.Find<Button>("Capture"));
        await app.Until(() => app.Current is CaptureViewModel, "the capture screen is shown");
    }

    [AvaloniaFact]
    public async Task No_breakdown_accompanies_the_total()
    {
        using var app = await DesktopHarness.Home(api => api.Respond("GET", "/purchases", HttpStatusCode.OK, new[]
        {
            Purchases.Make(1, "2026-09-10T10:00:00", 4m, ExtractionState.Extracted, merchantRaw: "Idea", lines: 2),
            Purchases.Make(2, "2026-08-10T10:00:00", 9m, merchantRaw: "Last month"),
        }));

        // Nothing a breakdown could be made of is even read, and the one figure not belonging to a
        // listed purchase is the month's total.
        Assert.Empty(app.Api.To("GET", "/categories"));
        var amountsShown = app.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && (text.Text ?? string.Empty).Contains('€', StringComparison.Ordinal))
            .Select(text => text.Name)
            .ToList();
        Assert.Equal(["MonthTotal", "RowAmount", "RowAmount"], amountsShown);
        Assert.DoesNotContain(app.Window.GetVisualDescendants(), visual => visual.GetType().Name.Contains("Chart", StringComparison.Ordinal));
    }

    private static double Area(Control control)
    {
        return control.Bounds.Width * control.Bounds.Height;
    }

    private static void AssertWithinWindow(DesktopHarness app, Control control)
    {
        Assert.True(control.IsEffectivelyVisible, $"'{control.Name}' is not visible.");
        var topLeft = control.TranslatePoint(default, app.Window)!.Value;
        var bottomRight = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), app.Window)!.Value;

        Assert.True(topLeft.X >= 0 && topLeft.Y >= 0, $"'{control.Name}' starts outside the window at {topLeft}.");
        Assert.True(
            bottomRight.X <= app.Window.ClientSize.Width && bottomRight.Y <= app.Window.ClientSize.Height,
            $"'{control.Name}' ends at {bottomRight}, outside a {app.Window.ClientSize} window.");
    }
}
