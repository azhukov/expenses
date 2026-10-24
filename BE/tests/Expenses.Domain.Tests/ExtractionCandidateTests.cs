using Expenses.Domain.Extraction;

namespace Expenses.Domain.Tests;

/// <summary>Scenarios from receipt-ingestion: "Verbatim receipt text is preserved".</summary>
public sealed class ExtractionCandidateTests
{
    [Fact]
    public void Unit_text_is_preserved_alongside_a_match()
    {
        var candidate = ExtractionCandidate.Propose(1, "Spinat", 1.99m, unitRaw: "Bund");

        candidate.MatchUnit(3);

        Assert.Equal(3, candidate.UnitId);
        Assert.Equal("Bund", candidate.UnitRaw);
    }
}
