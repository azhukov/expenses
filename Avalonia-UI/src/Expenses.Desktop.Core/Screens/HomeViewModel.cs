using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Navigation;
using Expenses.Desktop.Core.Rules;

namespace Expenses.Desktop.Core.Screens;

/// <summary>
/// A launcher, not a dashboard. Capture is the point of the screen; the month's figure and the recent
/// few are what make it possible to tell whether a purchase was already logged.
/// </summary>
public sealed partial class HomeViewModel(LedgerClient ledger, TimeProvider clock, IImagePicker picker, INavigator navigator) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>The report that purchases could not be loaded, in the ledger's words; null when they were.</summary>
    [ObservableProperty]
    public partial string? LoadFailure { get; private set; }

    [ObservableProperty]
    public partial string MonthTotal { get; private set; } = string.Empty;

    /// <summary>How many receipts need review, said as a sentence; null when none do, rather than a zero.</summary>
    [ObservableProperty]
    public partial string? ReviewNotice { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<RecentPurchaseRow> Recent { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    /// <summary>
    /// One read of the month answers the total, the review count and the recent few. The merchant
    /// dictionary is read beside it and its failure is swallowed on purpose: without it a row falls back
    /// to what the receipt printed, which is not a failure to load the ledger.
    /// </summary>
    [RelayCommand]
    private async Task Load(CancellationToken cancellationToken)
    {
        var now = clock.GetLocalNow().DateTime;
        var (from, to) = MonthRules.CurrentMonth(now);

        IsLoading = true;
        LoadFailure = null;
        IsEmpty = false;

        var merchants = MerchantsOrNone(cancellationToken);

        try
        {
            var month = await ledger.ListPurchases(from, to, cancellationToken);
            var names = await merchants;

            MonthTotal = Formatting.Amount(MonthRules.MonthTotal(month, now));
            ReviewNotice = MonthRules.ReviewCount(month, now) switch
            {
                0 => null,
                1 => "1 receipt needs review",
                var count => $"{count} receipts need review",
            };
            Recent = [.. MonthRules.Recent(month).Select(purchase => RowOf(purchase, names, now))];
            IsEmpty = Recent.Count == 0;
        }
        catch (LedgerException failure)
        {
            // The ledger's own words after the one sentence saying what could not be done.
            LoadFailure = $"Purchases could not be loaded. {failure.Message}";
            MonthTotal = Formatting.Amount(0m);
            ReviewNotice = null;
            Recent = [];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task Retry(CancellationToken cancellationToken)
    {
        return Load(cancellationToken);
    }

    /// <summary>Why a drop captured nothing; null otherwise.</summary>
    [ObservableProperty]
    public partial string? CaptureNotice { get; private set; }

    /// <summary>
    /// What was dropped onto the window, as files. Exactly one is captured; several are refused
    /// together; none - text, or anything that is not a file - changes nothing.
    /// </summary>
    public void AcceptDrop(IReadOnlyList<ReceiptImage> files)
    {
        switch (files.Count)
        {
            case 0:
                return;
            case 1:
                CaptureNotice = null;
                navigator.ToCapture(files[0]);
                return;
            default:
                CaptureNotice = "Receipts are captured one at a time. Drop a single file.";
                return;
        }
    }

    /// <summary>
    /// Opens the picker and hands a chosen file straight on, with no confirmation between. Home never
    /// reads the file, let alone submits it: that is the capture screen's job.
    /// </summary>
    [RelayCommand]
    private async Task Capture(CancellationToken cancellationToken)
    {
        if (await picker.PickImage(cancellationToken) is { } image)
        {
            CaptureNotice = null;
            navigator.ToCapture(image);
        }
    }

    private static RecentPurchaseRow RowOf(PurchaseView purchase, IReadOnlyList<MerchantView> merchants, DateTime now)
    {
        var lines = purchase.Expenses.Count == 1 ? "1 line" : $"{purchase.Expenses.Count} lines";

        return new RecentPurchaseRow(
            Labels.Merchant(purchase, merchants),
            Formatting.When(purchase.OccurredAt, now),
            lines,
            purchase.HasReceipt,
            Formatting.Amount(purchase.Amount));
    }

    private async Task<IReadOnlyList<MerchantView>> MerchantsOrNone(CancellationToken cancellationToken)
    {
        try
        {
            return await ledger.ListMerchants(cancellationToken);
        }
        catch (LedgerException)
        {
            return [];
        }
    }
}
