namespace Expenses.Application.Services;

/// <summary>
/// The one comparison form for printed unit text, applied identically wherever an alias is stored,
/// looked up or removed, and wherever printed text is matched against a unit code (design D3 of
/// add-unit-aliases). A static function over a string rather than a wrapper type (D7).
/// </summary>
public static class UnitText
{
    /// <summary>
    /// Ignores case, surrounding whitespace, and periods and slashes at either end; collapses runs of
    /// internal whitespace to one space; keeps every other character. Null when nothing is left.
    /// </summary>
    public static string? Normalise(string? text)
    {
        if (text is null)
        {
            return null;
        }

        // Repeated until stable, because the two trims uncover each other: " /kom/. " sheds its
        // spaces, then its slashes and period, and only then is "kom".
        string current = text;
        string previous;

        do
        {
            previous = current;
            current = current.Trim().Trim(s_endPunctuation);
        }
        while (current != previous);

        if (current.Length == 0)
        {
            return null;
        }

        return string.Join(' ', current.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private static readonly char[] s_endPunctuation = ['.', '/'];
}
