using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Navigation;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Only a desktop lifetime gets a window here; the headless test platform builds its own.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            _services = Composition.Services(LedgerAddress.Configuration(AppContext.BaseDirectory), () => window);
            window.DataContext = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services.Dispose();

            _services.GetRequiredService<INavigator>().ToHome();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
