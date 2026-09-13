using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Screens;

namespace Expenses.Desktop.Core.Navigation;

/// <summary>
/// Puts a fresh screen in the window's slot and starts what arriving on it means: home reads the
/// ledger, capture uploads its image (D5). A screen is never reused, so returning home after a
/// confirmation reads the month again rather than showing what was read before it.
/// </summary>
public sealed class Navigator(MainWindowViewModel shell, LedgerClient ledger, TimeProvider clock, IImagePicker picker) : INavigator
{
    public void ToHome()
    {
        var home = new HomeViewModel(ledger, clock, picker, this);
        shell.Current = home;
        home.LoadCommand.Execute(null);
    }

    public void ToCapture(ReceiptImage image)
    {
        var capture = new CaptureViewModel(ledger, this, image);
        shell.Current = capture;
        capture.StartCommand.Execute(null);
    }
}
