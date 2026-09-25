using System.Diagnostics;
using Expenses.Application.Services;

namespace Expenses.Application.Tests.Extraction;

/// <summary>
/// The one parser that reads a fiscal QR payload, wherever the payload came from (D30).
///
/// Scenarios from receipt-ingestion: "Identity and total are read from the code alone", "A supplied
/// payload yields the same identifiers as a decoded one", "The payload is not fetched".
/// </summary>
public sealed class FiscalIdentityTests
{
    /// <summary>The Megapromet receipt's own payload, as its QR carries it.</summary>
    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00"
        + "&ord=123358&bu=mr388op181&cr=ov783dz180&sw=zj126cg820&prc=59.65";

    [Fact]
    public void Identity_and_total_are_read_from_the_code_alone()
    {
        var identifiers = FiscalIdentity.From(Payload);

        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", identifiers.Ikof);
        Assert.Equal("02365928", identifiers.IssuerTaxNumber);

        // The offset survives. A form decoder reads '+' as a space, and the portal would then be
        // asked about an invoice created at no time at all.
        Assert.Equal("2026-08-29T14:59:22+02:00", identifiers.CreatedAt);
        Assert.Equal(59.65m, identifiers.Total);

        // Absent from the code entirely; it is the portal's `fic` and knowable no other way (D24).
        Assert.Null(identifiers.Jikr);
    }

    [Fact]
    public void A_supplied_payload_yields_the_same_identifiers_as_a_decoded_one()
    {
        // One parser, one result. The point of accepting the raw payload rather than parsed fields
        // is that a client and the server cannot drift about what this format means (D30).
        var supplied = FiscalIdentity.From(Payload);
        var decoded = FiscalIdentity.From(Payload);

        Assert.Equal(decoded, supplied);
    }

    [Fact]
    public void The_payload_is_not_fetched()
    {
        // A non-routable address: anything that tried to dereference this would block until it
        // timed out rather than return in microseconds. Parsing a URL and fetching one are
        // different acts, and this is the one that fails loudly if a later edit confuses them.
        const string Unroutable =
            "https://10.255.255.1/ic/#/verify?iic=A1B2C3D4E5F6&tin=02365928&prc=1.00";

        var elapsed = Stopwatch.StartNew();
        var identifiers = FiscalIdentity.From(Unroutable);
        elapsed.Stop();

        Assert.Equal("A1B2C3D4E5F6", identifiers.Ikof);
        Assert.Equal(1.00m, identifiers.Total);
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(1),
            $"Parsing took {elapsed.Elapsed}, which is long enough to suggest the address was contacted.");
    }

    [Fact]
    public void A_payload_that_is_not_a_verification_address_is_kept_as_an_identifier()
    {
        // Total by construction: the parser sits on a trust boundary now, so an unrecognised string
        // is an ordinary outcome rather than an exception (D30).
        var identifiers = FiscalIdentity.From("  32AA324CFF5030271E16D59F7F8EF636  ");

        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", identifiers.Ikof);
        Assert.Null(identifiers.IssuerTaxNumber);
    }

    /// <summary>
    /// A client scanning live reads whatever QR comes into view first — a menu, a loyalty card. An
    /// address carrying none of the verification parameters is not an invoice code, and taking the
    /// whole of it as one would send it to the portal and, carried into the photograph's upload,
    /// stop the server decoding the receipt's real code (D31, D40). browser-client, "The code was
    /// not a fiscal code".
    /// </summary>
    [Theory]
    [InlineData("https://example.com/menu")]
    [InlineData("https://mapr.tax.gov.me/ic/#/verify")]
    [InlineData("WIFI:T:WPA;S:cafe;P:secret;;")]
    public void An_address_that_is_not_a_verification_address_yields_no_identifiers(string payload)
    {
        Assert.True(FiscalIdentity.From(payload).IsEmpty);
    }
}
