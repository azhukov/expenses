using CommunityToolkit.Mvvm.ComponentModel;

namespace Expenses.Desktop.Core.Screens;

/// <summary>The window's one slot: the screen being shown (D5).</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ObservableObject? Current { get; set; }
}
