using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Expenses.Desktop.Core.Navigation;

namespace Expenses.Desktop;

/// <summary>
/// The operating system's file picker, offering images by default without preventing any other file
/// from being chosen: whether a file is a receipt the ledger can read is the ledger's judgement (D6).
/// </summary>
public sealed class StorageImagePicker(Func<TopLevel?> topLevel) : IImagePicker
{
    public async Task<ReceiptImage?> PickImage(CancellationToken cancellationToken = default)
    {
        if (topLevel() is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a receipt",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
        });

        return files.Count == 1 ? ImageOf(files[0]) : null;
    }

    /// <summary>A storage file as the screens know it: its name, and a way to open it that reads nothing yet.</summary>
    public static ReceiptImage ImageOf(IStorageFile file)
    {
        return new ReceiptImage(file.Name, async _ => await file.OpenReadAsync());
    }
}
