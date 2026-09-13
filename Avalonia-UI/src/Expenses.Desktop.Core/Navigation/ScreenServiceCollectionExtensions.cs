using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Screens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Desktop.Core.Navigation;

public static class ScreenServiceCollectionExtensions
{
    /// <summary>
    /// Everything the screens need except the file picker, which belongs to the window the app runs in
    /// and is registered by the app (D6).
    /// </summary>
    public static IServiceCollection AddDesktopScreens(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLedgerClient(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<INavigator>(provider => new Navigator(
            provider.GetRequiredService<MainWindowViewModel>(),
            provider.GetRequiredService<LedgerClient>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IImagePicker>()));

        return services;
    }
}
