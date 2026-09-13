using Expenses.Desktop.Core.Navigation;

namespace Expenses.Desktop.Core.Tests.Fakes;

public sealed class FakeNavigator : INavigator
{
    public int HomeCount { get; private set; }

    public List<ReceiptImage> Captures { get; } = [];

    public void ToHome()
    {
        HomeCount++;
    }

    public void ToCapture(ReceiptImage image)
    {
        Captures.Add(image);
    }
}
