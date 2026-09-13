using System.Net;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Tests.Fakes;
using Expenses.Desktop.UI.Tests.Harness;
using Expenses.Desktop.Views;

namespace Expenses.Desktop.UI.Tests;

/// <summary>
/// Scenarios from desktop-client, requirement "The client fits the window it is shown in": "No
/// horizontal scrolling", "The minimum window still leads with capture", "The keyboard reaches
/// everything", "Controls are named"; and "Entries are not controls" from "Recent purchases are listed
/// for recognition".
/// </summary>
public sealed class FitAndReachTests
{
    private static readonly PurchaseView[] s_month =
    [
        Purchases.Make(1, "2026-09-16T10:00:00", 30.25m, ExtractionState.NeedsReview, merchantRaw: "A merchant whose printed name runs on for rather longer than a narrow window is wide", lines: 3),
        Purchases.Make(2, "2026-09-10T10:00:00", 7m, merchantRaw: "CAFE BAR PEPE"),
    ];

    private static readonly CaptureResult s_needsReview = Captures.Extracted(
        [Captures.Candidate(1, "A description long enough to need the whole width of the line", 2m, confidence: new Dictionary<string, decimal> { ["description"] = 0.2m }), Captures.Candidate(2, "Bread", 1m)],
        total: 3.50m,
        merchantName: "Idea",
        state: ExtractionState.NeedsReview,
        checks: [Captures.Check("The lines add up to 3.00, not the total of 3.50, which is a sentence long enough to wrap in a narrow window.", CheckOutcome.Failed)]);

    [AvaloniaFact]
    public void The_window_cannot_be_made_smaller_than_a_minimum()
    {
        var window = new MainWindow();

        Assert.InRange(window.MinWidth, 320, 640);
        Assert.InRange(window.MinHeight, 400, 720);
    }

    [AvaloniaFact]
    public async Task The_minimum_window_still_leads_with_capture()
    {
        var minimum = new MainWindow();
        using var app = await DesktopHarness.Home(api => api.Respond("GET", "/purchases", HttpStatusCode.OK, s_month), minimum.MinWidth, minimum.MinHeight);

        AssertWithinWindow(app, app.Find<Button>("Capture"));
        AssertWithinWindow(app, app.Find<TextBlock>("MonthTotal"));
        AssertWithinWindow(app, app.FindAll<TextBlock>("RowMerchant")[0]);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_horizontal_scrolling_on_home(bool atMinimum)
    {
        var minimum = new MainWindow();
        using var app = await DesktopHarness.Home(
            api => api.Respond("GET", "/purchases", HttpStatusCode.OK, s_month),
            atMinimum ? minimum.MinWidth : 1400,
            atMinimum ? minimum.MinHeight : 900);

        AssertNothingWiderThanTheWindow(app);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_horizontal_scrolling_on_review(bool atMinimum)
    {
        var minimum = new MainWindow();
        using var app = await Reviewing(s_needsReview, atMinimum ? minimum.MinWidth : 1400, atMinimum ? minimum.MinHeight : 900);

        AssertNothingWiderThanTheWindow(app);
    }

    [AvaloniaFact]
    public async Task The_keyboard_reaches_everything_on_home()
    {
        using var app = await DesktopHarness.Home(api => api.Unreachable("GET", "/purchases"));

        await AssertTabVisitsEveryInteractiveControl(app);
    }

    [AvaloniaFact]
    public async Task The_keyboard_reaches_everything_on_review()
    {
        using var app = await Reviewing(s_needsReview);

        await AssertTabVisitsEveryInteractiveControl(app);
    }

    [AvaloniaFact]
    public async Task Controls_are_named_on_home_and_review()
    {
        using var home = await DesktopHarness.Home(api => api.Unreachable("GET", "/purchases"));
        AssertEveryInteractiveControlIsNamed(home);

        using var review = await Reviewing(s_needsReview);
        AssertEveryInteractiveControlIsNamed(review);
    }

    [AvaloniaFact]
    public async Task Entries_are_not_controls()
    {
        using var app = await DesktopHarness.Home(api => api.Respond("GET", "/purchases", HttpStatusCode.OK, s_month));
        var requests = app.Api.Requests.Count;
        var home = app.Current;
        var recent = app.Find<ItemsControl>("Recent");

        Assert.IsNotAssignableFrom<SelectingItemsControl>(recent);
        Assert.All(recent.GetVisualDescendants().OfType<InputElement>(), element => Assert.False(element.Focusable, $"{element.GetType().Name} in a recent entry can take focus."));
        Assert.DoesNotContain(recent.GetVisualDescendants(), visual => visual is Button);

        var row = app.FindAll<TextBlock>("RowMerchant")[1];
        await app.Click(row);
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(centre, MouseButton.Left);
        app.Window.MouseUp(centre, MouseButton.Left);
        app.Window.MouseDown(centre, MouseButton.Left);
        app.Window.MouseUp(centre, MouseButton.Left);
        await app.Press(Key.Enter);
        await app.Settle();

        Assert.Same(home, app.Current);
        Assert.Equal(requests, app.Api.Requests.Count);
        Assert.Equal(0, app.Picker.Opened);
    }

    private static IReadOnlyList<Control> InteractiveControls(DesktopHarness app)
    {
        return
        [
            .. app.Window.GetVisualDescendants()
                .OfType<Control>()
                .Where(control => control is Button or TextBox or ComboBox && control.IsEffectivelyVisible && control.IsEffectivelyEnabled)
                .Where(control => !control.GetVisualAncestors().OfType<Control>().Any(ancestor => ancestor is Button or TextBox or ComboBox)),
        ];
    }

    private static async Task AssertTabVisitsEveryInteractiveControl(DesktopHarness app)
    {
        var expected = InteractiveControls(app);
        Assert.NotEmpty(expected);
        var visited = new List<Control>();

        app.Window.Focus();
        for (var step = 0; step < expected.Count * 3 && visited.Count < expected.Count; step++)
        {
            await app.Press(Key.Tab);

            var focused = app.Window.FocusManager?.GetFocusedElement() as Control;
            var owner = focused is null ? null : expected.FirstOrDefault(control => control == focused || focused.GetVisualAncestors().Contains(control));

            if (owner is not null && !visited.Contains(owner))
            {
                Assert.True(focused!.Classes.Contains(":focus-visible"), $"'{owner.Name}' has keyboard focus but does not show it.");
                visited.Add(owner);
            }
        }

        var missed = expected.Except(visited).Select(control => control.Name ?? control.GetType().Name);
        Assert.True(!missed.Any(), $"The keyboard never reached: {string.Join(", ", missed)}.");
    }

    private static void AssertEveryInteractiveControlIsNamed(DesktopHarness app)
    {
        Assert.All(InteractiveControls(app), control => Assert.False(
            string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)),
            $"'{control.Name}' exposes no name to assistive technology."));
    }

    private static void AssertNothingWiderThanTheWindow(DesktopHarness app)
    {
        var width = app.Window.ClientSize.Width;

        // A text box scrolling its own text is a field, not the screen scrolling sideways, so its
        // internal scroller is not held to this.
        foreach (var scroll in app.Window.GetVisualDescendants().OfType<ScrollViewer>().Where(each => each.IsEffectivelyVisible && each.TemplatedParent is not TextBox))
        {
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, $"'{scroll.Name}' holds content {scroll.Extent.Width} wide in a {scroll.Viewport.Width} viewport.");
        }

        foreach (var control in app.Window.GetVisualDescendants().OfType<Control>().Where(each => each.IsEffectivelyVisible && each.Bounds.Width > 0))
        {
            var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), app.Window)!.Value.X;
            Assert.True(right <= width + 0.5, $"{control.GetType().Name} '{control.Name}' reaches {right} in a window {width} wide.");
        }
    }

    private static void AssertWithinWindow(DesktopHarness app, Control control)
    {
        var topLeft = control.TranslatePoint(default, app.Window)!.Value;
        var bottomRight = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), app.Window)!.Value;

        Assert.True(control.IsEffectivelyVisible && topLeft.X >= 0 && topLeft.Y >= 0, $"'{control.Name}' starts outside the window at {topLeft}.");
        Assert.True(bottomRight.X <= app.Window.ClientSize.Width && bottomRight.Y <= app.Window.ClientSize.Height, $"'{control.Name}' ends at {bottomRight}, outside a {app.Window.ClientSize} window.");
    }

    private static async Task<DesktopHarness> Reviewing(CaptureResult capture, double width = 900, double height = 700)
    {
        var app = await DesktopHarness.Home(width: width, height: height);
        app.Api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, capture);
        app.Navigator.ToCapture(FakeImagePicker.Image("receipt.jpg"));
        await app.Until(() => app.IsShown("Confirm"), "the review is shown");

        return app;
    }
}
