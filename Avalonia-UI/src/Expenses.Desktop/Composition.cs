using Avalonia.Controls;
using Expenses.Desktop.Core.Navigation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Desktop;

/// <summary>The composition root: the screens, the ledger client, and the picker of the window they run in.</summary>
public static class Composition
{
    /// <param name="configuration">Where the ledger's address is read from.</param>
    /// <param name="topLevel">The window whose storage provider the picker opens.</param>
    /// <param name="replace">Registrations applied last, so a test can swap the transport or the picker.</param>
    public static ServiceProvider Services(IConfiguration configuration, Func<TopLevel?> topLevel, Action<IServiceCollection>? replace = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IImagePicker>(new StorageImagePicker(topLevel));
        services.AddDesktopScreens(configuration);
        replace?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
