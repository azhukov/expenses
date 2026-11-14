namespace Expenses.Application.Services;

/// <summary>
/// Money from a source that states it more precisely than the ledger keeps it, brought to the cent
/// (D7). The fiscal portal states each weighed line unrounded, as quantity times unit price
/// (1.696 kg × 2.30 = 3.9008), and rounds only the invoice's totals. These functions are the one
/// place that turns such values into cents, so that every reader of them agrees.
/// </summary>
public static class CentRounding
{
    /// <summary>
    /// To the cent, half away from zero, as a till prints it: 0.125 is 0.13. Not .NET's default,
    /// which rounds half to even and would print 0.12.
    /// </summary>
    public static decimal ToCents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Each line to the cent, then adjusted so that the lines sum to <paramref name="total"/>.
    ///
    /// The adjustment happens only where the exact lines already agree with the total once rounded,
    /// so the difference is purely an artefact of rounding each line separately. It is spread one
    /// cent per line, to the lines whose rounding moved them furthest the other way (the largest
    /// remainder method), earliest line first on a tie. No line moves by more than a cent, and none
    /// goes below zero. Lines that disagree with the total for real are only rounded, and are left
    /// for the arithmetic check to report rather than forced to agree. With no total, each line is
    /// only rounded.
    /// </summary>
    public static IReadOnlyList<decimal> ToCentsSummingTo(IReadOnlyList<decimal> exact, decimal? total)
    {
        decimal[] rounded = [.. exact.Select(ToCents)];

        if (total is not { } stated || ToCents(exact.Sum()) != stated)
        {
            return rounded;
        }

        int cents = (int)((stated - rounded.Sum()) * 100m);

        if (cents == 0)
        {
            return rounded;
        }

        // How far rounding moved each line: positive where it was rounded down, negative where up.
        // A missing cent goes to the line rounded down the most, and a cent too many comes off the
        // line rounded up the most, so each line ends as close to its exact value as the total allows.
        var order = Enumerable.Range(0, exact.Count)
            .Select(index => (Index: index, Remainder: exact[index] - rounded[index]))
            .Where(line => cents > 0 || rounded[line.Index] >= 0.01m);

        var chosen = cents > 0
            ? order.OrderByDescending(line => line.Remainder).ThenBy(line => line.Index)
            : order.OrderBy(line => line.Remainder).ThenBy(line => line.Index);

        decimal step = cents > 0 ? 0.01m : -0.01m;

        foreach (var line in chosen.Take(Math.Abs(cents)))
        {
            rounded[line.Index] += step;
        }

        return rounded;
    }
}
