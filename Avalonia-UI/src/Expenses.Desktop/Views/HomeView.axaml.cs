using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Expenses.Desktop.Core.Screens;

namespace Expenses.Desktop.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>The files in a drop, and nothing else: text, or a folder, is not a receipt image.</summary>
    public static IReadOnlyList<IStorageFile> FilesIn(IDataTransfer data)
    {
        return [.. (data.TryGetFiles() ?? []).OfType<IStorageFile>()];
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = FilesIn(e.DataTransfer).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    // The decision about what a drop means belongs to the view model; the view only turns the platform's
    // drop into files (D6).
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is HomeViewModel home)
        {
            home.AcceptDrop([.. FilesIn(e.DataTransfer).Select(StorageImagePicker.ImageOf)]);
        }
    }
}
