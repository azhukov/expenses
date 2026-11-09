using System.Globalization;
using System.Text;
using Expenses.Api;
using Expenses.Application.Dtos;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// ledger-export: "An export is encoded in a fixed CSV dialect" and the header of "An export has one
/// row per expense line". Pure functions with no database; they sit here because the writer lives in
/// the API project, which has no test project of its own.
/// </summary>
public sealed class LedgerCsvTests
{
    private static readonly byte[] s_bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void The_header_row()
    {
        byte[] written = LedgerCsv.Write([]);

        Assert.Equal(s_bom, written[..3]);
        Assert.Equal(
            "purchase_id,occurred_at,merchant,description,quantity,unit,unit_price,list_unit_price,discount_amount,category,amount\r\n",
            Text(written));
    }

    [Fact]
    public void Rows_end_with_CRLF_and_carry_every_column_in_order()
    {
        string text = Text(LedgerCsv.Write([
            Line(description: "Apples", merchant: "Voli", unit: "kg", category: "Groceries", quantity: 1.25m, unitPrice: 3.2m, listUnitPrice: 3.6m, discountAmount: 0.5m, amount: 4m),
        ]));

        Assert.Equal(
            "purchase_id,occurred_at,merchant,description,quantity,unit,unit_price,list_unit_price,discount_amount,category,amount\r\n"
            + "7,2026-10-14 13:05:00,Voli,Apples,1.25,kg,3.20,3.60,0.50,Groceries,4.00\r\n",
            text);
    }

    [Fact]
    public void Text_with_commas_and_quotes_survives()
    {
        string text = Text(LedgerCsv.Write([Line(description: "Juice \"Orange\", 1L")]));

        Assert.Contains(",\"Juice \"\"Orange\"\", 1L\",", text, StringComparison.Ordinal);
        Assert.Equal("Juice \"Orange\", 1L", Parse(text)[1][3]);
    }

    [Theory]
    [InlineData("Two\nlines")]
    [InlineData("Two\r\nlines")]
    public void A_field_containing_a_line_break_is_quoted(string description)
    {
        string text = Text(LedgerCsv.Write([Line(description: description)]));

        Assert.Contains($",\"{description}\",", text, StringComparison.Ordinal);
        Assert.Equal(description, Parse(text)[1][3]);
    }

    [Fact]
    public void Non_Latin_text_survives()
    {
        byte[] written = LedgerCsv.Write([Line(description: "Hljeb, čokolada, Ćevapi", merchant: "Маркет")]);

        Assert.Equal(s_bom, written[..3]);
        var row = Parse(Text(written))[1];
        Assert.Equal("Hljeb, čokolada, Ćevapi", row[3]);
        Assert.Equal("Маркет", row[2]);
    }

    [Theory]
    [InlineData("sr-Latn-ME")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Numbers_use_an_invariant_form(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            var row = Parse(Text(LedgerCsv.Write([
                Line(quantity: 1.25m, unitPrice: 3.2m, amount: 4m, listUnitPrice: 1234.5m, discountAmount: 0.05m),
            ])))[1];

            Assert.Equal("1.25", row[4]);
            Assert.Equal("3.20", row[6]);
            Assert.Equal("1234.50", row[7]);
            Assert.Equal("0.05", row[8]);
            Assert.Equal("4.00", row[10]);
            Assert.Equal("2026-10-14 13:05:00", row[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Changed during apply: the test first expected <c>2.50</c> for <c>2.50m</c>, "as recorded". The
    /// column stores three places whatever was supplied, so a stored quantity arrives as <c>1.000</c>
    /// and its original scale cannot be recovered; trailing zeros are dropped instead, for a value
    /// held in memory and a value read back alike.
    /// </summary>
    [Fact]
    public void Quantities_are_written_without_trailing_zeros()
    {
        var rows = Parse(Text(LedgerCsv.Write([
            Line(quantity: 1m), Line(quantity: 1.000m), Line(quantity: 0.375m), Line(quantity: 2.50m), Line(quantity: 12.340m),
        ])));

        Assert.Equal(["1", "1", "0.375", "2.5", "12.34"], rows.Skip(1).Select(row => row[4]));
    }

    [Fact]
    public void Occurrence_is_written_as_recorded()
    {
        var occurred = new DateTime(2026, 10, 14, 13, 5, 0, DateTimeKind.Unspecified);

        var row = Parse(Text(LedgerCsv.Write([Line(occurredAt: occurred)])))[1];

        Assert.Equal("2026-10-14 13:05:00", row[1]);
    }

    [Fact]
    public void A_purchase_recorded_by_date_alone()
    {
        var row = Parse(Text(LedgerCsv.Write([Line(occurredAt: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Unspecified))])))[1];

        Assert.Equal("2026-10-14 00:00:00", row[1]);
    }

    [Fact]
    public void Null_optional_fields_are_written_empty()
    {
        string text = Text(LedgerCsv.Write([Line()]));

        Assert.EndsWith("\r\n7,2026-10-14 13:05:00,,Line,1,,,,,,2.00\r\n", text, StringComparison.Ordinal);
    }

    private static ExportLine Line(
        string description = "Line",
        string? merchant = null,
        string? unit = null,
        string? category = null,
        decimal quantity = 1m,
        decimal? unitPrice = null,
        decimal? listUnitPrice = null,
        decimal? discountAmount = null,
        decimal amount = 2m,
        DateTime? occurredAt = null)
        => new(
            7,
            occurredAt ?? new DateTime(2026, 10, 14, 13, 5, 0, DateTimeKind.Unspecified),
            merchant,
            description,
            quantity,
            unit,
            unitPrice,
            listUnitPrice,
            discountAmount,
            category,
            amount);

    /// <summary>The bytes after the byte-order mark, which a reader consumes rather than shows.</summary>
    private static string Text(byte[] written) => new UTF8Encoding(false).GetString(written, 3, written.Length - 3);

    /// <summary>
    /// A minimal RFC 4180 reader, written independently of the writer so that a round trip is a real
    /// check: quoted fields may hold commas, doubled quotes and line breaks; rows end in CRLF.
    /// </summary>
    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
                i++;
            }
            else
            {
                field.Append(c);
            }
        }

        return rows;
    }
}
