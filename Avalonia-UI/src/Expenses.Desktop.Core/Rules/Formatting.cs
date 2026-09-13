using System.Globalization;

namespace Expenses.Desktop.Core.Rules;

/// <summary>
/// Presentation only: nothing here rounds or recomputes a value (ported from FE/src/format, D7). The
/// locale and currency are constants, so there is one place to change when they become configurable,
/// and they never follow the computer's regional settings - the ledger is in euro wherever it is read.
/// </summary>
public static class Formatting
{
    private static readonly CultureInfo s_locale = CultureInfo.GetCultureInfo("en-GB");

    private static readonly NumberFormatInfo s_euro = Euro();

    /// <summary>An amount in euro, always with two decimal places. Zero is shown, never omitted.</summary>
    public static string Amount(decimal value)
    {
        return value.ToString("C2", s_euro);
    }

    /// <summary>
    /// When a purchase occurred, said the way a reader would say it. Today and yesterday are named;
    /// anything older is a date. Both values are compared as the wall-clock dates they carry, whatever
    /// <see cref="DateTime.Kind"/> says, so no time zone ever moves a purchase to another day.
    /// </summary>
    public static string When(DateTime occurredAt, DateTime now)
    {
        return (now.Date - occurredAt.Date).Days switch
        {
            0 => "Today",
            1 => "Yesterday",
            _ => occurredAt.ToString("d MMM yyyy", s_locale),
        };
    }

    private static NumberFormatInfo Euro()
    {
        var format = (NumberFormatInfo)s_locale.NumberFormat.Clone();
        format.CurrencySymbol = "€";

        return NumberFormatInfo.ReadOnly(format);
    }
}
