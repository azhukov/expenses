using Expenses.Domain.Extraction;

namespace Expenses.Domain.Tests;

/// <summary>
/// Scenarios from receipt-ingestion: "Verbatim receipt text is preserved", and the inferred mark of
/// "Extracted unit text is matched to a unit".
/// </summary>
public sealed class ExtractionCandidateTests
{
    [Fact]
    public void An_inferred_unit_is_carried_and_marked_as_inferred()
    {
        var candidate = ExtractionCandidate.Propose(1, "Pfand", 0.10m, quantity: 2m, unitRaw: "Pfand");

        candidate.InferUnit(1);

        Assert.Equal(1, candidate.UnitId);
        Assert.True(candidate.UnitInferred);
        Assert.Equal("Pfand", candidate.UnitRaw);
    }

    [Fact]
    public void A_matched_unit_is_not_marked_as_inferred()
    {
        var candidate = ExtractionCandidate.Propose(1, "Spinat", 1.99m, unitRaw: "Bund");

        candidate.MatchUnit(3);

        Assert.False(candidate.UnitInferred);
    }

    [Fact]
    public void Unit_text_is_preserved_alongside_a_match()
    {
        var candidate = ExtractionCandidate.Propose(1, "Spinat", 1.99m, unitRaw: "Bund");

        candidate.MatchUnit(3);

        Assert.Equal(3, candidate.UnitId);
        Assert.Equal("Bund", candidate.UnitRaw);
    }
}
