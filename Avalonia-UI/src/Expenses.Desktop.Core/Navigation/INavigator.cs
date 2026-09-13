namespace Expenses.Desktop.Core.Navigation;

/// <summary>The two screens and the one hand-off between them (D5).</summary>
public interface INavigator
{
    /// <summary>Shows a fresh home screen, which reads the ledger again.</summary>
    void ToHome();

    /// <summary>Shows the capture screen for one image.</summary>
    void ToCapture(ReceiptImage image);
}
