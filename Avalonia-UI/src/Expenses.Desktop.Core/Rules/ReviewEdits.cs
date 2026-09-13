namespace Expenses.Desktop.Core.Rules;

/// <summary>
/// What the user is asserting about the receipt, as against what extraction proposed.
/// <see cref="DateEdited"/> is false while the date is still the one the receipt established, which
/// the API resolves by itself.
/// </summary>
public sealed record ReviewEdits(
    IReadOnlyList<EditableLine> Lines,
    string Amount,
    string Merchant,
    string OccurredAt,
    bool DateEdited);
