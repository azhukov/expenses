using Expenses.Domain.Entities;
using ExtractionState = Expenses.Domain.Entities.Receipt.ExtractionState;

namespace Expenses.Domain.Tests;

/// <summary>Scenarios from receipt-ingestion: "Extraction lifecycle", "Extraction can be re-run".</summary>
public sealed class ReceiptTests
{
    private static Receipt AnImage(ExtractionState state = ExtractionState.Extracted, string? failureReason = null)
        => Receipt.Of("ab/cd/abcd.jpg", "image/jpeg", sizeInBytes: 2_000_000, state, failureReason);

    [Fact]
    public void A_receipt_is_constructed_already_extracted()
    {
        var image = AnImage(ExtractionState.Extracted);

        Assert.Equal(ExtractionState.Extracted, image.State);
        Assert.Null(image.FailureReason);
    }

    [Fact]
    public void A_receipt_is_constructed_needing_review()
    {
        var image = AnImage(ExtractionState.NeedsReview);

        Assert.Equal(ExtractionState.NeedsReview, image.State);
    }

    [Fact]
    public void A_receipt_is_constructed_failed_with_its_reason()
    {
        var image = AnImage(ExtractionState.Failed, "engine returned no result");

        Assert.Equal(ExtractionState.Failed, image.State);
        Assert.Equal("engine returned no result", image.FailureReason);
    }

    [Theory]
    [InlineData(ExtractionState.Extracted, ExtractionState.NeedsReview)]
    [InlineData(ExtractionState.NeedsReview, ExtractionState.Extracted)]
    [InlineData(ExtractionState.Failed, ExtractionState.Extracted)]
    [InlineData(ExtractionState.Extracted, ExtractionState.Failed)]
    public void Re_running_moves_directly_between_terminal_states(ExtractionState from, ExtractionState to)
    {
        var image = AnImage(from, from == ExtractionState.Failed ? "first attempt" : null);

        image.TransitionTo(to, to == ExtractionState.Failed ? "second attempt" : null);

        Assert.Equal(to, image.State);
        Assert.Equal(to == ExtractionState.Failed ? "second attempt" : null, image.FailureReason);
    }

    [Fact]
    public void Re_running_a_failed_extraction_clears_the_previous_reason_on_success()
    {
        var image = AnImage(ExtractionState.Failed, "engine returned no result");

        image.TransitionTo(ExtractionState.Extracted);

        Assert.Equal(ExtractionState.Extracted, image.State);
        Assert.Null(image.FailureReason);
    }

    [Fact]
    public void A_receipt_requires_the_storage_key_of_its_file()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Receipt.Of("  ", "image/jpeg", sizeInBytes: 1, ExtractionState.Extracted));

        Assert.Equal("storageKey", error.ParamName);
    }

    [Fact]
    public void The_storage_key_is_retained_as_written()
    {
        // Stored rather than recomputed from the content, so the layout of the store can change
        // without invalidating existing references (D11).
        Assert.Equal("ab/cd/abcd.jpg", AnImage().StorageKey);
    }
}
