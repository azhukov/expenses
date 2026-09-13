using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Navigation;

namespace Expenses.Desktop.Core.Screens;

/// <summary>
/// The capture screen: it uploads the image it was handed the moment it arrives, waits out the
/// extraction the endpoint runs synchronously, and hands the result to review.
/// </summary>
public sealed partial class CaptureViewModel(LedgerClient ledger, INavigator navigator, ReceiptImage image) : ObservableObject
{
    private bool _started;
    private bool _left;
    private byte[]? _bytes;

    public string FileName => image.Name;

    [ObservableProperty]
    public partial bool IsUploading { get; private set; }

    /// <summary>The report that the receipt could not be uploaded; null otherwise.</summary>
    [ObservableProperty]
    public partial string? UploadFailure { get; private set; }

    /// <summary>The review of a completed capture; null until the capture response arrives.</summary>
    [ObservableProperty]
    public partial ReviewViewModel? Review { get; private set; }

    /// <summary>
    /// Run when the screen is shown. Guarded, not merely idempotent in intent: a screen shown, redrawn
    /// or re-activated must not become a second receipt uploaded for one file, a duplicate the user
    /// never asked for and would only discover later.
    /// </summary>
    [RelayCommand]
    private Task Start(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;

        return Upload(cancellationToken);
    }

    [RelayCommand]
    private Task Retry(CancellationToken cancellationToken)
    {
        return Upload(cancellationToken);
    }

    /// <summary>
    /// Reads the file once, on the first attempt, and uploads from that buffer every time after: a
    /// retry sends the bytes that were chosen, even if the file has since changed or gone (D6).
    /// </summary>
    private async Task Upload(CancellationToken cancellationToken)
    {
        UploadFailure = null;
        IsUploading = true;

        try
        {
            _bytes ??= await ReadAll(cancellationToken);

            var result = await ledger.CaptureReceipt(image.Name, _bytes, cancellationToken);

            if (_left)
            {
                return;
            }

            // The response is held, never re-fetched: the server keeps nothing about a capture once it
            // has answered, so this is the only copy of the extraction outcome that exists.
            var review = new ReviewViewModel(ledger, navigator, result);
            await review.Load(cancellationToken);

            if (!_left)
            {
                Review = review;
            }
        }
        catch (OperationCanceledException) when (_left)
        {
        }
        catch (LedgerException failure) when (!_left)
        {
            UploadFailure = $"The receipt could not be uploaded. {failure.Message}";
        }
        catch (Exception failure) when (!_left && failure is IOException or UnauthorizedAccessException)
        {
            UploadFailure = "The receipt could not be uploaded. The file could not be read.";
        }
        catch (LedgerException) when (_left)
        {
        }
        finally
        {
            IsUploading = false;
        }
    }

    private async Task<byte[]> ReadAll(CancellationToken cancellationToken)
    {
        await using var stream = await image.Open(cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    /// <summary>
    /// Leaves without confirming. An upload still outstanding is cancelled, and in case the transport
    /// answers anyway, whatever it answers is discarded: a response arriving after the user has gone must
    /// not open a review of a capture they walked away from.
    /// </summary>
    [RelayCommand]
    private void Back()
    {
        _left = true;
        StartCommand.Cancel();
        RetryCommand.Cancel();
        navigator.ToHome();
    }
}
