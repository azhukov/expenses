using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Extraction;

/// <summary>
/// The one test in the suite that is allowed to call the real Claude API — everything else runs
/// against <see cref="ClaudeVisionStub"/> by design (D26, D34), so nothing here ever runs in CI or
/// costs money on its own. It exists to answer a question a stub cannot: does the model, given a
/// real photographed receipt, actually read it.
///
/// To run it locally: set <c>Extraction__Vision__ApiKey</c> to a real key (the same variable
/// docker-compose and `dotnet run` already read), then remove the <c>Skip</c> below and run this
/// test by name. Put the <c>Skip</c> back before committing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClaudeVisionGoldenPathTests(PostgresFixture postgres) : IAsyncLifetime
{
    public Task InitializeAsync() => postgres.Migrate();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Skip = "Manual only: calls the real Claude API and costs money. Set Extraction__Vision__ApiKey " +
      "and remove this Skip to run it against api.anthropic.com.")]
    public async Task The_real_engine_reads_a_real_photographed_receipt()
    {
        string apiKey = Environment.GetEnvironmentVariable("Extraction__Vision__ApiKey")
            ?? throw new InvalidOperationException(
                "Set Extraction__Vision__ApiKey before running this test with its Skip removed.");

        await using var services = postgres.Services(("Extraction:Vision:ApiKey", apiKey));

        var extractor = services.GetRequiredService<IReceiptExtractor>();
        var image = DecoderRegressionTests.Photograph(DecoderRegressionTests.DecodableReceipt);

        var result = await extractor.Extract(image, FiscalIdentifiers.None);

        Assert.NotNull(result);
        Assert.Equal("claude-vision", result.EngineName);
        Assert.NotEmpty(result.Candidates);
        Assert.NotNull(result.Total);

        // A wine line at roughly the right price is the loosest possible sign the model actually
        // read the till receipt rather than hallucinating a plausible-looking one.
        Assert.Contains(result.Candidates, candidate => candidate.Amount is > 0m and < 100m);
    }
}
