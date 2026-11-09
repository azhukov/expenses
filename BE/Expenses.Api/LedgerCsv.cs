using System.Globalization;
using System.Text;
using Expenses.Application.Dtos;

namespace Expenses.Api;

/// <summary>
/// Encodes export rows in the ledger's one CSV dialect (D3 of add-csv-export): UTF-8 with a
/// byte-order mark, comma-separated, CRLF rows, RFC 4180 quoting, invariant numbers, money to two
/// decimals, quantities as recorded, and occurrence as <c>yyyy-MM-dd HH:mm:ss</c> with no
/// conversion. Which rows, and what they say, is decided in Application; this only spells them.
/// Hand-written rather than a library, because a library's culture and quoting defaults are exactly
/// what the dialect pins down.
/// </summary>
public static class LedgerCsv
{
    /// <summary>The export's columns, in order.</summary>
    public const string Header =
        "purchase_id,occurred_at,merchant,description,quantity,unit,unit_price,list_unit_price,discount_amount,category,amount";

    private const string RowEnd = "\r\n";

    /// <summary>
    /// With the byte-order mark: without it, Excel reads the file as the machine's legacy code page
    /// and garbles every č, ć and Cyrillic letter.
    /// </summary>
    private static readonly UTF8Encoding s_encoding = new(encoderShouldEmitUTF8Identifier: true);

    private static readonly char[] s_needsQuoting = [',', '"', '\r', '\n'];

    public static byte[] Write(IReadOnlyList<ExportLine> lines)
    {
        var text = new StringBuilder(Header).Append(RowEnd);

        foreach (var line in lines)
        {
            text.Append(line.PurchaseId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(line.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
                .Append(Field(line.Merchant)).Append(',')
                .Append(Field(line.Description)).Append(',')
                .Append(Quantity(line.Quantity)).Append(',')
                .Append(Field(line.Unit)).Append(',')
                .Append(Money(line.UnitPrice)).Append(',')
                .Append(Money(line.ListUnitPrice)).Append(',')
                .Append(Money(line.DiscountAmount)).Append(',')
                .Append(Field(line.Category)).Append(',')
                .Append(Money(line.Amount))
                .Append(RowEnd);
        }

        return [.. s_encoding.GetPreamble(), .. s_encoding.GetBytes(text.ToString())];
    }

    /// <summary>Two decimals always: the value is exact to two places, so this pads and never rounds (D7).</summary>
    private static string Money(decimal? amount)
        => amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Without trailing zeros. The column stores three places whatever was supplied, so the scale a
    /// quantity was recorded with does not survive storage, and "1.000 pcs" would claim a precision
    /// nobody gave. Three optional places is all a quantity can carry (D10), so this drops zeros and
    /// never a significant digit.
    /// </summary>
    private static string Quantity(decimal quantity)
        => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Quoted only when it has to be, with inner quotes doubled (RFC 4180).</summary>
    private static string Field(string? value)
        => value is null || value.IndexOfAny(s_needsQuoting) < 0
            ? value ?? string.Empty
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
