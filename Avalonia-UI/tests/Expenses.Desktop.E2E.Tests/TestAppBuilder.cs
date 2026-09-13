using Avalonia;
using Avalonia.Headless;
using Expenses.Desktop.E2E.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Expenses.Desktop.E2E.Tests;

/// <summary>
/// The real application on the headless platform: no window server, so the suite runs on a CI agent
/// with no display (D8).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
