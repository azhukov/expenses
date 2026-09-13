using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Expenses.Desktop.Core.Navigation;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;
using Expenses.Desktop.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Expenses.Desktop.UI.Tests.Harness;

/// <summary>
/// The real window, views and composition on the headless platform, with the file picker replaced and,
/// unless it is driving the real ledger, the HTTP transport and the clock too. Controls are found by
/// name and driven the way a person drives them: clicks at their centre, keys, typed text (D8).
/// </summary>
public sealed class DesktopHarness : IDisposable
{
    public static readonly DateTime Now = new(2026, 9, 17, 10, 0, 0);

    private readonly ServiceProvider _services;

    private DesktopHarness(double width, double height, IConfiguration? realLedger)
    {
        Api
            .Respond("GET", "/purchases", HttpStatusCode.OK, "[]")
            .Respond("GET", "/merchants", HttpStatusCode.OK, "[]")
            .Respond("GET", "/categories", HttpStatusCode.OK, "[]")
            .Respond("GET", "/units", HttpStatusCode.OK, "[]");

        Window = new MainWindow { Width = width, Height = height };

        _services = Composition.Services(realLedger ?? new ConfigurationBuilder().Build(), () => Window, services =>
        {
            services.RemoveAll<IImagePicker>();
            services.AddSingleton<IImagePicker>(Picker);

            if (realLedger is null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = Api));
            }
        });

        Window.DataContext = _services.GetRequiredService<MainWindowViewModel>();
    }

    /// <summary>The client's own ledger client, for reading back what the screens wrote.</summary>
    public Core.Ledger.LedgerClient Ledger => _services.GetRequiredService<Core.Ledger.LedgerClient>();

    /// <summary>
    /// A window showing home against the ledger the configuration addresses, with the real clock. Only
    /// the file picker is faked, because a headless window has no operating system dialog to open.
    /// </summary>
    public static async Task<DesktopHarness> HomeAgainst(IConfiguration ledger, double width = 1000, double height = 800)
    {
        var harness = new DesktopHarness(width, height, ledger);
        harness.Window.Show();
        harness.Navigator.ToHome();
        await harness.Settle();

        return harness;
    }

    public FakeHttpMessageHandler Api { get; } = new();

    public FakeImagePicker Picker { get; } = new();

    public MainWindow Window { get; }

    public INavigator Navigator => _services.GetRequiredService<INavigator>();

    public object? Current => _services.GetRequiredService<MainWindowViewModel>().Current;

    /// <summary>A window of the given size showing home, with the ledger arranged before it is read.</summary>
    public static async Task<DesktopHarness> Home(Action<FakeHttpMessageHandler>? arrange = null, double width = 900, double height = 700)
    {
        var harness = new DesktopHarness(width, height, realLedger: null);
        arrange?.Invoke(harness.Api);
        harness.Window.Show();
        harness.Navigator.ToHome();
        await harness.Settle();

        return harness;
    }

    public T Find<T>(string name)
        where T : Control
    {
        return FindAll<T>(name).FirstOrDefault() ?? throw new Xunit.Sdk.XunitException($"No visible {typeof(T).Name} named '{name}' is on screen.");
    }

    public IReadOnlyList<T> FindAll<T>(string name)
        where T : Control
    {
        return [.. Window.GetVisualDescendants().OfType<T>().Where(control => control.Name == name && control.IsEffectivelyVisible)];
    }

    public bool IsShown(string name)
    {
        return Window.GetVisualDescendants().OfType<Control>().Any(control => control.Name == name && control.IsEffectivelyVisible);
    }

    public async Task Click(Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new Xunit.Sdk.XunitException($"'{control.Name}' is not in the window.");

        Window.MouseDown(centre, MouseButton.Left);
        Window.MouseUp(centre, MouseButton.Left);
        await Settle();
    }

    /// <summary>Replaces a text box's contents as a person would: focus it, select everything, type.</summary>
    public async Task Type(TextBox box, string text)
    {
        await Click(box);
        Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        Window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        Window.KeyTextInput(text);
        await Settle();
    }

    public async Task Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        await Settle();
    }

    /// <summary>Runs the dispatcher until the condition holds, failing after the timeout (two seconds by default).</summary>
    public async Task Until(Func<bool> condition, string because, int timeoutMilliseconds = 2000)
    {
        for (var attempt = 0; attempt < timeoutMilliseconds / 10; attempt++)
        {
            await Settle();

            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException($"Timed out waiting until {because}.");
    }

    public async Task Settle()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        Window.Close();
        _services.Dispose();
    }
}
