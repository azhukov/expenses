using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Views;

namespace Expenses.Desktop;

/// <summary>
/// Which view draws which screen. Listed explicitly rather than found by name through reflection, so
/// that trimming cannot remove a view nobody visibly references (D5).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            HomeViewModel => new HomeView(),
            CaptureViewModel => new CaptureView(),
            ReviewViewModel => new ReviewView(),
            _ => null,
        };
    }

    public bool Match(object? data)
    {
        return data is HomeViewModel or CaptureViewModel or ReviewViewModel;
    }
}
