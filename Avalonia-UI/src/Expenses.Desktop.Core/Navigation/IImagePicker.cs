namespace Expenses.Desktop.Core.Navigation;

/// <summary>The operating system's file picker, behind a seam the headless tests replace (D6).</summary>
public interface IImagePicker
{
    /// <summary>One chosen file, or null where the picker was dismissed.</summary>
    Task<ReceiptImage?> PickImage(CancellationToken cancellationToken = default);
}
