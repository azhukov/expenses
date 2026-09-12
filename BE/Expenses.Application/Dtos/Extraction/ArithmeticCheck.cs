namespace Expenses.Application.Dtos;

/// <summary>
/// One check, carrying the values that disagreed so the reason survives to the user
/// rather than being reduced to a boolean.
/// </summary>
public sealed record ArithmeticCheck(
    string Name,
    CheckOutcome Outcome,
    string Description,
    IReadOnlyDictionary<string, decimal?> Values)
{
    private static readonly IReadOnlyDictionary<string, decimal?> s_noValues =
        new Dictionary<string, decimal?>();

    public static ArithmeticCheck NotApplicable(string name, string description)
        => new(name, CheckOutcome.NotApplicable, description, s_noValues);

    // Records compare a dictionary field by reference, which would make two identically
    // computed reports unequal. The values are part of the check, so they are compared as such.
    public bool Equals(ArithmeticCheck? other)
        => other is not null
        && Name == other.Name
        && Outcome == other.Outcome
        && Description == other.Description
        && Values.Count == other.Values.Count
        && Values.All(entry => other.Values.TryGetValue(entry.Key, out decimal? value) && value == entry.Value);

    public override int GetHashCode() => HashCode.Combine(Name, Outcome, Description, Values.Count);
}
