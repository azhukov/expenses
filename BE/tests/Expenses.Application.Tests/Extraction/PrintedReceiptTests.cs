using Expenses.Application.Dtos;
using Expenses.Application.Services;

namespace Expenses.Application.Tests.Extraction;

/// <summary>
/// Four photographed receipts from Budva, each as a reader copies it off the paper: every number
/// with the decimal places the till printed, list prices per unit, discounts as the till printed
/// them. Each must come out in cents (D7) and pass the arithmetic oracle (D20) once it has been
/// brought to the shape the ledger keeps, and a misread digit must still be caught.
///
/// Each till exposes something different: Aroma prints discounts as percentages, on weighed lines
/// too, and taxes at two rates; IDEA prints weighed lines to four places and taxes them before
/// rounding; the restaurant rounds VAT line by line; Kuca Hemije prints a 0.00% discount on every
/// line.
/// </summary>
public sealed class PrintedReceiptTests
{
    /// <summary>Aroma 34, Budva, 29.09.2026 13:48:34 — "Za uplatu EUR 20,19", PDV 0,37 at 7% and 2,53 at 21%.</summary>
    private static readonly PrintedLine[] s_aroma =
    [
        new(4.49m, 1m, 4.4885m, 8.50m, DiscountPercent: 47.19m),      // Sladoled Oreo Mini Sticks
        new(0.79m, 1m, 0.79m, 0.89m, DiscountPercent: 11.24m),        // Mlijeko Milkos sterilno
        new(2.02m, 2.042m, 0.99m),                                    // Krompir mladi
        new(1.75m, 1m, 1.75m),                                        // Pavlaka kisjela
        new(3.09m, 1m, 3.09m),                                        // Jaja Agromont XL
        new(0.35m, 0.088m, 3.99m),                                    // Luk mladi
        new(1.75m, 1.358m, 1.2896m, 1.59m, DiscountPercent: 18.89m),  // Sljiva
        new(1.00m, 0.674m, 1.49m),                                    // Paprika silja crvena
        new(1.66m, 0.832m, 1.99m),                                    // Paradajz
        new(3.29m, 0.942m, 3.49m),                                    // Jabuka pink lady
    ];

    /// <summary>IDEA Budva Rozino, 21.10.2026 20:18:55 — TOTAL 9,59, PDV 1,67 at 21%.</summary>
    private static readonly PrintedLine[] s_idea =
    [
        new(2.2886m, 1.536m, 1.49m),  // Jabuka Grinsmit uvozna
        new(4.0500m, 1m, 4.05m),      // Sir 375gr Zagrebacki Dukat
        new(1.2179m, 0.612m, 1.99m),  // Kruska Viljamovka
        new(2.0381m, 1.206m, 1.69m),  // Banana
    ];

    /// <summary>Food Stories, Budva, 05.11.2026 15:14:20 — Ukupno 12,10, osnovica 10,53, PDV 1,57 at 15%.</summary>
    private static readonly PrintedLine[] s_restaurant =
    [
        new(4.84m, 0.242m, 20.00m),  // Vinaigrette sa pasulj
        new(2.10m, 0.14m, 15.00m),   // Pire od krompira
        new(5.16m, 0.172m, 30.00m),  // Svinjski eskalop u sosu od pecur
    ];

    /// <summary>Kuca Hemije 11, Budva, 26.09.2026 09:20:41 — TOTAL 30.66, osnovica 25.34, PDV 5.32 at 21%.</summary>
    private static readonly PrintedLine[] s_kucaHemije =
    [
        new(3.49m, 1m, 3.49m, 3.49m, DiscountPercent: 0m),     // Paloma toalet rolna
        new(19.99m, 1m, 19.99m, 19.99m, DiscountPercent: 0m),  // Fairy kapsule 113pcs
        new(4.99m, 1m, 4.99m, 4.99m, DiscountPercent: 0m),     // Finish cistac 250 ml
        new(2.19m, 1m, 2.19m, 2.19m, DiscountPercent: 0m),     // Royal vreca sa vrpcom
    ];

    public static TheoryData<string> Receipts => ["aroma", "idea", "restaurant", "kuca-hemije"];

    private static (PrintedLine[] Lines, decimal Total, decimal? Rate, decimal Tax) Receipt(string name) => name switch
    {
        "aroma" => (s_aroma, 20.19m, null, 2.90m),
        "idea" => (s_idea, 9.59m, 21m, 1.67m),
        "restaurant" => (s_restaurant, 12.10m, 15m, 1.57m),
        "kuca-hemije" => (s_kucaHemije, 30.66m, 21m, 5.32m),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static ExtractionArithmetic Arithmetic(
        IReadOnlyList<LineInCents> lines,
        decimal total,
        decimal? rate,
        decimal tax)
        => new(
            [.. lines.Select((line, index) => new ExtractedLineAmounts(index + 1, line.Amount, line.ListPrice, line.Discount))],
            total,
            rate,
            tax);

    private static ArithmeticValidationReport Validate(string name, decimal? taxOverride = null)
    {
        var (lines, total, rate, tax) = Receipt(name);

        return ArithmeticValidator.Validate(
            Arithmetic(PrintedLineMoney.ToCents(lines, total), total, rate, taxOverride ?? tax));
    }

    private static ArithmeticCheck Check(ArithmeticValidationReport report, string name)
        => Assert.Single(report.Checks, check => check.Name == name);

    private static bool InCents(decimal value) => value == Math.Round(value, 2);

    [Theory]
    [MemberData(nameof(Receipts))]
    public void Every_receipt_passes_every_check_it_can_be_given(string name)
    {
        var report = Validate(name);

        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    [Theory]
    [MemberData(nameof(Receipts))]
    public void Every_receipt_arrives_in_cents_and_sums_to_its_total(string name)
    {
        var (lines, total, _, _) = Receipt(name);

        var money = PrintedLineMoney.ToCents(lines, total);

        Assert.Equal(total, money.Sum(line => line.Amount));
        Assert.All(money, line =>
        {
            Assert.True(InCents(line.Amount), $"amount {line.Amount}");
            Assert.True(line.UnitPrice is null || InCents(line.UnitPrice.Value), $"unit price {line.UnitPrice}");
            Assert.True(line.ListPrice is null || InCents(line.ListPrice.Value), $"list price {line.ListPrice}");
            Assert.True(line.Discount is null || InCents(line.Discount.Value), $"discount {line.Discount}");
        });
    }

    /// <summary>
    /// Rounded one by one, 2.2886, 4.05, 1.2179 and 2.0381 sum to 9.60 against a printed 9.59.
    /// The cent comes off the line rounding moved furthest up, the pear.
    /// </summary>
    [Fact]
    public void Ideas_four_place_lines_are_brought_to_its_total()
    {
        var money = PrintedLineMoney.ToCents(s_idea, 9.59m);

        Assert.Equal([2.29m, 4.05m, 1.21m, 2.04m], money.Select(line => line.Amount));
    }

    /// <summary>"Cijena bez popusta: 8,50 Popust: 47,19%" — 8.50 less 4.01 is the 4.49 paid.</summary>
    [Fact]
    public void A_discount_printed_as_a_percentage_arrives_as_the_amount_taken_off()
    {
        var oreo = PrintedLineMoney.ToCents(s_aroma, 20.19m)[0];

        Assert.Equal(8.50m, oreo.ListPrice);
        Assert.Equal(4.01m, oreo.Discount);
        Assert.Equal(4.49m, oreo.UnitPrice);
    }

    /// <summary>
    /// "Cijena bez popusta: 1,59 Popust: 18,89%" over 1.358 kg. The list price the check compares
    /// against the line's 1.75 is the whole line's, 2.16, not the kilogram price.
    /// </summary>
    [Fact]
    public void A_weighed_discounted_line_lists_at_its_whole_price()
    {
        var sljiva = PrintedLineMoney.ToCents(s_aroma, 20.19m)[6];

        Assert.Equal(2.16m, sljiva.ListPrice);
        Assert.Equal(0.41m, sljiva.Discount);
        Assert.Equal(1.75m, sljiva.Amount);
    }

    [Fact]
    public void A_line_with_no_printed_discount_carries_neither_half_of_one()
    {
        var money = PrintedLineMoney.ToCents([new PrintedLine(1.75m, 1m, 1.75m, ListUnitPrice: 1.75m)], 1.75m);

        Assert.Null(money[0].ListPrice);
        Assert.Null(money[0].Discount);
    }

    [Fact]
    public void A_discount_printed_as_an_amount_is_taken_as_printed()
    {
        var money = PrintedLineMoney.ToCents([new PrintedLine(4.49m, 1m, ListUnitPrice: 8.50m, DiscountAmount: 4.01m)], 4.49m);

        Assert.Equal(8.50m, money[0].ListPrice);
        Assert.Equal(4.01m, money[0].Discount);
    }

    /// <summary>Aroma taxes at 7% and at 21%, so there is no one rate to check its 2.90 against.</summary>
    [Fact]
    public void A_receipt_taxed_at_two_rates_has_no_tax_check()
    {
        Assert.Equal(CheckOutcome.NotApplicable, Check(Validate("aroma"), ArithmeticChecks.TotalTax).Outcome);
    }

    /// <summary>4.84, 2.10 and 5.16 at 15% are 0.63, 0.27 and 0.67 of VAT each — 1.57 — where 12.10 implies 1.58.</summary>
    [Fact]
    public void Tax_rounded_line_by_line_agrees_with_the_total()
    {
        Assert.Equal(CheckOutcome.Passed, Check(Validate("restaurant"), ArithmeticChecks.TotalTax).Outcome);
    }

    /// <summary>9.5946 at 21% implies 1.67, where the rounded 9.59 implies 1.66.</summary>
    [Fact]
    public void Tax_on_unrounded_lines_agrees_with_the_total()
    {
        Assert.Equal(CheckOutcome.Passed, Check(Validate("idea"), ArithmeticChecks.TotalTax).Outcome);
    }

    [Theory]
    [InlineData("restaurant", 1.67)]
    [InlineData("restaurant", 1.51)]
    [InlineData("idea", 1.76)]
    [InlineData("kuca-hemije", 5.82)]
    [InlineData("kuca-hemije", 5.62)]
    public void A_misread_tax_digit_is_still_caught(string name, decimal misread)
    {
        Assert.Equal(CheckOutcome.Failed, Check(Validate(name, misread), ArithmeticChecks.TotalTax).Outcome);
    }

    /// <summary>
    /// The price of accepting both tills above: on a receipt of n lines the tax may sit up to half a
    /// cent per line from what the total implies, so a last digit misread by one is not caught by
    /// the tax check on a receipt of more than one line. It is pinned here so that loosening the
    /// slack further is a visible decision.
    /// </summary>
    [Fact]
    public void A_tax_a_cent_out_on_a_four_line_receipt_is_within_the_slack()
    {
        Assert.Equal(CheckOutcome.Passed, Check(Validate("kuca-hemije", 5.33m), ArithmeticChecks.TotalTax).Outcome);
        Assert.Equal(CheckOutcome.Failed, Check(Validate("kuca-hemije", 5.35m), ArithmeticChecks.TotalTax).Outcome);
    }

    [Fact]
    public void A_misread_digit_in_a_discounted_weighed_line_is_caught()
    {
        var lines = s_aroma.ToArray();
        lines[6] = lines[6] with { Amount = 1.15m };

        var report = ArithmeticValidator.Validate(
            Arithmetic(PrintedLineMoney.ToCents(lines, 20.19m), 20.19m, null, 2.90m));

        Assert.Equal(CheckOutcome.Failed, Check(report, ArithmeticChecks.LineSum).Outcome);
        var discount = Check(report, ArithmeticChecks.LineDiscount);
        Assert.Equal(CheckOutcome.Failed, discount.Outcome);
        Assert.Equal(7m, discount.Values["lineNumber"]);
    }

    [Fact]
    public void A_misread_discount_percentage_is_caught()
    {
        var lines = s_aroma.ToArray();
        lines[0] = lines[0] with { DiscountPercent = 41.19m };

        var report = ArithmeticValidator.Validate(
            Arithmetic(PrintedLineMoney.ToCents(lines, 20.19m), 20.19m, null, 2.90m));

        var discount = Check(report, ArithmeticChecks.LineDiscount);
        Assert.Equal(CheckOutcome.Failed, discount.Outcome);
        Assert.Equal(1m, discount.Values["lineNumber"]);
    }
}
