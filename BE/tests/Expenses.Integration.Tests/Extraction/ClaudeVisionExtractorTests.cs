using System.Net;
using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
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

    [Fact]
    public async Task The_engine_cannot_be_reached()
    {
        await using var stub = await ClaudeVisionStub.Hanging();
        await using var services = Services(stub, ("Extraction:Vision:TimeoutMilliseconds", "250"));

        Assert.Null(await Extractor(services).Extract(s_image, FiscalIdentifiers.None));
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
