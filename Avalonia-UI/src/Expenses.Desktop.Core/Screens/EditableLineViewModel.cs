using CommunityToolkit.Mvvm.ComponentModel;
using Expenses.Desktop.Core.Rules;

namespace Expenses.Desktop.Core.Screens;

/// <summary>
/// One line on the review screen. Its numbers are the text the user typed, so a half-typed "3." stays
/// as typed; the raw receipt texts and the prices the user cannot edit travel through untouched.
/// </summary>
public sealed partial class EditableLineViewModel : ObservableObject
{
    private readonly EditableLine _seed;

    public EditableLineViewModel(EditableLine seed, string? unsure)
    {
        _seed = seed;
        Unsure = unsure;
        Description = seed.Description;
        Amount = seed.Amount;
        Quantity = seed.Quantity;
        CategoryCode = seed.CategoryCode;
        UnitCode = seed.UnitCode;
    }

    public int Key => _seed.Key;

    /// <summary>What extraction was unsure of on this line, said as a sentence; null when nothing.</summary>
    public string? Unsure { get; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string Amount { get; set; }

    [ObservableProperty]
    public partial string Quantity { get; set; }

    [ObservableProperty]
    public partial string CategoryCode { get; set; }

    [ObservableProperty]
    public partial string UnitCode { get; set; }

    public EditableLine ToLine()
    {
        return _seed with
        {
            Description = Description,
            Amount = Amount,
            Quantity = Quantity,
            CategoryCode = CategoryCode ?? string.Empty,
            UnitCode = UnitCode ?? string.Empty,
        };
    }
}
