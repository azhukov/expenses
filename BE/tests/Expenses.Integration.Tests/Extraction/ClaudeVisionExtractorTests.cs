using System.Net;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// The real vision adapter, against <see cref="ClaudeVisionStub"/> rather than the real Claude API
/// (D26, D34). Scenarios from receipt-ingestion: "The engine reads the image content", "Results are
/// identified by engine", "An unverifiable value carries the engine's own confidence", "The engine
/// cannot be reached", "The engine's answer cannot be interpreted", "No credential is configured".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClaudeVisionExtractorTests(PostgresFixture postgres)
{
    private static readonly ReceiptImageContent s_image = new(7, "image/jpeg", [0xFF, 0xD8, 0xFF, 0xD9]);

    [Fact]
    public async Task The_engine_reads_the_image_content()
    {
        await using var first = await ClaudeVisionStub.Answering("claude-vision-response.json");
        await using var second = await ClaudeVisionStub.Answering("claude-vision-response-alternate.json");

        await using var firstServices = Services(first);
        await using var secondServices = Services(second);

        var firstResult = await Extractor(firstServices).Extract(s_image, FiscalIdentifiers.None);
        var secondResult = await Extractor(secondServices).Extract(s_image, FiscalIdentifiers.None);

        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Equal("Vision Fixture Market", firstResult.MerchantName);
        Assert.Equal("Alternate Fixture Bakery", secondResult.MerchantName);
        Assert.NotEqual(
            firstResult.Candidates.Select(candidate => candidate.Description),
            secondResult.Candidates.Select(candidate => candidate.Description));
    }

    /// <summary>
    /// A wiring regression test, not a new behaviour: default configuration (no
    /// `Extraction:Vision:Engine` override) must resolve the real adapter, not
    /// `PlaceholderReceiptExtractor` (D33).
    /// </summary>
    [Fact]
    public async Task The_real_adapter_is_registered_by_default()
    {
        await using var services = postgres.Services();

        Assert.Equal("claude-vision", services.GetRequiredService<IReceiptExtractor>().EngineName);
    }

    [Fact]
    public async Task Results_are_identified_by_engine()
    {
        await using var stub = await ClaudeVisionStub.Answering();
        await using var services = Services(stub);

        var result = await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        Assert.NotNull(result);
        Assert.Equal("claude-vision", result.EngineName);
        Assert.Equal("claude-sonnet-5-test", result.EngineVersion);
    }

    [Fact]
    public async Task An_unverifiable_value_carries_the_engines_own_confidence()
    {
        await using var stub = await ClaudeVisionStub.Answering();
        await using var services = Services(stub);

        var result = await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        Assert.NotNull(result);
        Assert.Equal(0.92m, result.ReportedConfidence[ExtractedValues.MerchantName]);

        var first = result.Candidates[0];
        Assert.Equal(0.95m, first.ReportedConfidence[ExtractedValues.Description]);
        Assert.False(first.ReportedConfidence.ContainsKey(ExtractedValues.Amount));
    }

    /// <summary>
    /// The printed purchase time is read off the image as wall-clock time, unconverted (D5), so a
    /// receipt with no fiscal code still arrives at review with its date.
    /// </summary>
    [Fact]
    public async Task The_printed_date_and_time_are_read()
    {
        await using var stub = await ClaudeVisionStub.Answering();
        await using var services = Services(stub);

        var result = await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 8, 24, 12, 50, 8, DateTimeKind.Unspecified), result.OccurredAt);
        Assert.Equal(DateTimeKind.Unspecified, result.OccurredAt!.Value.Kind);
        Assert.True(result.Provenance.ContainsKey(ExtractedValues.OccurredAt));
    }

    /// <summary>A receipt whose date the engine could not read still has its lines.</summary>
    [Fact]
    public async Task A_receipt_without_a_readable_date_still_has_its_lines()
    {
        await using var stub = await ClaudeVisionStub.Answering("claude-vision-response-alternate.json");
        await using var services = Services(stub);

        var result = await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        Assert.NotNull(result);
        Assert.Null(result.OccurredAt);
        Assert.NotEmpty(result.Candidates);
    }

    /// <summary>
    /// IDEA prints weighed lines to four places (1.536 kg × 1.49 = 2.2886) under a total of 9.59. The
    /// ledger refuses money beyond the cent (D7), so a candidate that kept 2.2886 could never be
    /// confirmed; rounded line by line they would sum to 9.60.
    /// </summary>
    [Fact]
    public async Task Lines_read_to_four_places_arrive_in_cents_summing_to_the_total()
    {
        var result = await ExtractFrom("claude-vision-response-idea.json");

        Assert.Equal([2.29m, 4.05m, 1.21m, 2.04m], result.Candidates.Select(line => line.Amount));
        Assert.Equal(9.59m, result.Total);
        AssertPasses(result);
    }

    /// <summary>
    /// Aroma prints "Cijena bez popusta: 1,59 Popust: 18,89%" over 1.358 kg: a kilogram list price
    /// and a percentage. The candidate carries the line's list price, 2.16, and the 0.41 taken off
    /// it, which is what the discount check compares against the 1.75 paid.
    /// </summary>
    [Fact]
    public async Task A_percentage_discount_on_a_weighed_line_arrives_as_line_amounts()
    {
        var result = await ExtractFrom("claude-vision-response-aroma.json");

        var oreo = result.Candidates[0];
        Assert.Equal(4.49m, oreo.UnitPrice);
        Assert.Equal(8.50m, oreo.ListUnitPrice);
        Assert.Equal(4.01m, oreo.DiscountAmount);

        var sljiva = result.Candidates[6];
        Assert.Equal(2.16m, sljiva.ListUnitPrice);
        Assert.Equal(0.41m, sljiva.DiscountAmount);
        Assert.Equal(1.75m, sljiva.Amount);

        Assert.Null(result.Candidates[2].ListUnitPrice);
        Assert.Null(result.Candidates[2].DiscountAmount);
        AssertPasses(result);
    }

    /// <summary>
    /// The recorded fixture lists two bottles of water at 2.50 each, 5.00 for the line. Read per
    /// unit, the list price disagreed with its own line before it was extended by the quantity.
    /// </summary>
    [Fact]
    public async Task A_list_price_per_unit_is_extended_to_the_line()
    {
        var result = await ExtractFrom("claude-vision-response.json");

        Assert.Equal(5.00m, result.Candidates[0].ListUnitPrice);
        AssertPasses(result);
    }

    [Fact]
    public async Task The_engine_cannot_be_reached()
    {
        await using var stub = await ClaudeVisionStub.Hanging();
        await using var services = Services(stub, ("Extraction:Vision:TimeoutMilliseconds", "250"));

        Assert.Null(await Extractor(services).Extract(s_image, FiscalIdentifiers.None));
    }

    /// <summary>
    /// A poor photograph: one line's text is legible but not its amount, another's amount but not
    /// its text, and a third shows nothing. What was read is kept, the rest left empty, and the line
    /// with nothing read is dropped rather than proposed blank.
    /// </summary>
    [Fact]
    public async Task A_poor_photograph_yields_what_could_be_read()
    {
        await using var stub = await ClaudeVisionStub.Answering("claude-vision-response-blurry.json");
        await using var services = Services(stub);

        var result = await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal("Mlijeko 1l", result.Candidates[0].Description);
        Assert.Null(result.Candidates[0].Amount);
        Assert.Null(result.Candidates[1].Description);
        Assert.Equal(3.20m, result.Candidates[1].Amount);
        Assert.Equal(9.40m, result.Total);
        Assert.Null(result.MerchantName);
        Assert.Null(result.OccurredAt);

        // Provenance names only what vision actually read.
        Assert.False(result.Candidates[0].Provenance.ContainsKey(ExtractedValues.Amount));
        Assert.False(result.Candidates[1].Provenance.ContainsKey(ExtractedValues.Description));
        Assert.False(result.Provenance.ContainsKey(ExtractedValues.MerchantName));
    }

    [Fact]
    public async Task A_photograph_with_no_legible_line_still_yields_its_header()
    {
        await using var stub = await ClaudeVisionStub.Answering("claude-vision-response-header-only.json");
        await using var services = Services(stub);

        var result = await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        Assert.NotNull(result);
        Assert.Empty(result.Candidates);
        Assert.Equal("Blurred Market", result.MerchantName);
        Assert.Equal(12.40m, result.Total);
        Assert.Equal(new DateTime(2026, 8, 24, 12, 50, 0), result.OccurredAt);
    }

    [Fact]
    public async Task The_engines_answer_cannot_be_interpreted()
    {
        await using var stub = await ClaudeVisionStub.WithUnexpectedShape();
        await using var services = Services(stub);

        Assert.Null(await Extractor(services).Extract(s_image, FiscalIdentifiers.None));
    }

    [Fact]
    public async Task No_credential_is_configured()
    {
        await using var stub = await ClaudeVisionStub.Answering();
        await using var services = Services(stub, ("Extraction:Vision:ApiKey", string.Empty));

        Assert.Null(await Extractor(services).Extract(s_image, FiscalIdentifiers.None));
        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task The_engine_answers_with_a_failure(HttpStatusCode status)
    {
        await using var stub = await ClaudeVisionStub.Failing(status);
        await using var services = Services(stub);

        Assert.Null(await Extractor(services).Extract(s_image, FiscalIdentifiers.None));
    }

    [Fact]
    public async Task The_outbound_request_carries_the_image_and_model_id()
    {
        await using var stub = await ClaudeVisionStub.Answering();
        await using var services = Services(stub);

        await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("claude-sonnet-5-test", request.GetProperty("model").GetString());

        var content = request.GetProperty("messages")[0].GetProperty("content");
        var imageBlock = content[0];
        Assert.Equal("image", imageBlock.GetProperty("type").GetString());
        Assert.Equal(
            Convert.ToBase64String(s_image.Content),
            imageBlock.GetProperty("source").GetProperty("data").GetString());
    }

    private async Task<ExtractionStepResult> ExtractFrom(string fixture)
    {
        await using var stub = await ClaudeVisionStub.Answering(fixture);
        await using var services = Services(stub);

        var result = await Extractor(services).Extract(s_image, FiscalIdentifiers.None);

        return Assert.IsType<ExtractionStepResult>(result);
    }

    private static void AssertPasses(ExtractionStepResult result)
    {
        var report = ArithmeticValidator.Validate(ExtractionArithmetic.From(result));
        Assert.True(report.Passed, string.Join("; ", report.Failures.Select(check => check.Description)));
    }

    private static IReceiptExtractor Extractor(ServiceProvider services)
        => services.GetRequiredService<IReceiptExtractor>();

    private ServiceProvider Services(ClaudeVisionStub stub, params (string Key, string Value)[] settings)
        => postgres.Services(
        [
            ("Extraction:Vision:BaseAddress", stub.BaseAddress),
            ("Extraction:Vision:ApiKey", "test-key"),
            ("Extraction:Vision:ModelId", "claude-sonnet-5-test"),
            .. settings,
        ]);
}
