using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Navigation;
using Expenses.Desktop.Core.Rules;

namespace Expenses.Desktop.Core.Screens;

/// <summary>
/// The review screen. It holds the capture result untouched beside the user's edits and never
/// re-derives one from the other: the server keeps nothing about a capture after answering.
/// </summary>
public sealed partial class ReviewViewModel(LedgerClient ledger, INavigator navigator, CaptureResult capture) : ObservableObject
{
    private bool _seeded;
    private bool _dateEdited;
    private int _nextKey = 1;

    public CaptureResult Capture { get; } = capture;

    /// <summary>Why extraction failed, where it did; null otherwise.</summary>
    public string? FailureReason => Capture.State == ExtractionState.Failed ? Capture.FailureReason : null;

    /// <summary>Why this capture wants a human, in the response's own words.</summary>
    public IReadOnlyList<string> Reasons { get; } = ReviewRules.Reasons(capture);

    public ObservableCollection<EditableLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryChoices))]
    public partial IReadOnlyList<CategoryView> Categories { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitChoices))]
    public partial IReadOnlyList<UnitView> Units { get; private set; } = [];

    /// <summary>The category dictionary with "none" first, so a match can be cleared as well as chosen.</summary>
    public IReadOnlyList<Choice> CategoryChoices => [new(string.Empty, Labels.UnknownCategory), .. Categories.Select(category => new Choice(category.Code, category.Name))];

    public IReadOnlyList<Choice> UnitChoices => [new(string.Empty, "None"), .. Units.Select(unit => new Choice(unit.Code, unit.Name))];

    [ObservableProperty]
    public partial string Merchant { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OccurredAt { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Amount { get; set; } = string.Empty;

    /// <summary>
    /// Reads the category and unit dictionaries, then seeds the fields from the capture, once. After
    /// that the fields are the user's, and seeding again would silently discard an edit. A dictionary
    /// that cannot be read leaves its matches unselected, with the receipt's raw text still carried.
    /// </summary>
    public async Task Load(CancellationToken cancellationToken = default)
    {
        if (_seeded)
        {
            return;
        }

        var categories = OrNone(ledger.ListCategories(cancellationToken));
        var units = OrNone(ledger.ListUnits(cancellationToken));
        Categories = await categories;
        Units = await units;

        if (_seeded)
        {
            return;
        }

        var candidates = Capture.Result?.Candidates ?? [];

        foreach (var candidate in candidates)
        {
            var unsure = ReviewRules.LowConfidence(candidate.ReportedConfidence);
            Lines.Add(new EditableLineViewModel(
                ReviewRules.LineOf(candidate, Categories, Units),
                unsure.Count == 0 ? null : $"Extraction was unsure of {string.Join(", ", unsure)}."));
        }

        if (Lines.Count == 0)
        {
            Lines.Add(new EditableLineViewModel(ReviewRules.EmptyLine(1), null));
        }

        _nextKey = Lines.Max(line => line.Key) + 1;
        Merchant = Capture.Result?.MerchantName ?? string.Empty;
        Amount = Capture.Result?.Total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        OccurredAt = ReviewRules.DateText(ReviewRules.FiscalCreatedAt(Capture));
        _seeded = true;
    }

    /// <summary>What the user is asserting now, as the confirmation would be built from it.</summary>
    public ReviewEdits Edits()
    {
        return new ReviewEdits([.. Lines.Select(line => line.ToLine())], Amount, Merchant, OccurredAt, _dateEdited);
    }

    private static async Task<IReadOnlyList<T>> OrNone<T>(Task<IReadOnlyList<T>> read)
    {
        try
        {
            return await read;
        }
        catch (LedgerException)
        {
            return [];
        }
    }

    partial void OnOccurredAtChanged(string value)
    {
        // Only a change after seeding is the user's: until then the date is the receipt's, which the
        // ledger resolves by itself.
        if (_seeded)
        {
            _dateEdited = true;
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(new EditableLineViewModel(ReviewRules.EmptyLine(_nextKey++), null));
    }

    [RelayCommand]
    private void RemoveLine(EditableLineViewModel line)
    {
        Lines.Remove(line);
    }

    /// <summary>Why the last confirmation was not accepted, in the ledger's words; null otherwise.</summary>
    [ObservableProperty]
    public partial string? Rejection { get; private set; }

    /// <summary>True from the moment a confirmation is sent until it is answered; the confirm action is disabled throughout.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool IsConfirming { get; private set; }

    /// <summary>
    /// Submits the user's values with the capture's own outcome. A rejection leaves everything where it
    /// is, the capture with it: re-entering a receipt because the total was a cent out is not a thing to
    /// ask of anyone. A second activation while the first is outstanding does nothing, even from a caller
    /// that ignores CanExecute. The command deliberately takes no cancellation token: the toolkit answers
    /// a repeated execution of a cancellable command by cancelling the one already running, which here
    /// would abandon a confirmation the ledger may already have recorded.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task Confirm()
    {
        if (IsConfirming)
        {
            return;
        }

        IsConfirming = true;
        Rejection = null;

        try
        {
            var confirmation = ReviewRules.Confirmation(Capture, Edits());

            if (confirmation.Request is null)
            {
                Rejection = confirmation.Problem;
                return;
            }

            await ledger.RecordPurchase(confirmation.Request);
        }
        catch (LedgerException rejected)
        {
            Rejection = rejected.Message;
            return;
        }
        finally
        {
            IsConfirming = false;
        }

        // A fresh home reads the ledger again, so the month's total, its review count and the recent few
        // all include the new purchase without any of them being patched by hand.
        navigator.ToHome();
    }

    /// <summary>
    /// Leaves without confirming. Nothing is deleted: an unconfirmed capture is swept by the server's own
    /// daily cleanup, and the client never asks for it.
    /// </summary>
    [RelayCommand]
    private void Back()
    {
        navigator.ToHome();
    }

    private bool CanConfirm()
    {
        return !IsConfirming;
    }
}
