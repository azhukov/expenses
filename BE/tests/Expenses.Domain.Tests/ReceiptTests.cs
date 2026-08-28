using ExtractionState = Expenses.Domain.Receipt.ExtractionState;

namespace Expenses.Domain.Tests;

/// <summary>Scenarios from receipt-ingestion: "Extraction lifecycle", "Extraction can be re-run".</summary>
public sealed class ReceiptTests
{
    private static Receipt AnImage() =>
        Receipt.Of(new byte[32], "ab/cd/abcd.jpg", "image/jpeg", sizeInBytes: 2_000_000);

    [Fact]
    public void State_on_upload_is_pending()
    {
        Assert.Equal(ExtractionState.Pending, AnImage().State);
    }

    [Fact]
    public void Content_hash_must_be_a_sha256()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Receipt.Of(new byte[16], "ab/cd/abcd.jpg", "image/jpeg", sizeInBytes: 1));

        Assert.Equal("contentHash", error.ParamName);
    }

    [Fact]
    public void Successful_extraction()
    {
        var image = AnImage();

        image.TransitionTo(ExtractionState.Extracting);
        image.TransitionTo(ExtractionState.Extracted);

        Assert.Equal(ExtractionState.Extracted, image.State);
    }

    [Fact]
    public void Extraction_that_does_not_add_up_enters_needs_review()
    {
        var image = AnImage();

        image.TransitionTo(ExtractionState.Extracting);
        image.TransitionTo(ExtractionState.NeedsReview);

        Assert.Equal(ExtractionState.NeedsReview, image.State);
    }

    [Fact]
    public void Failed_extraction_records_its_reason()
    {
        var image = AnImage();

        image.TransitionTo(ExtractionState.Extracting);
        image.TransitionTo(ExtractionState.Failed, "engine returned no result");

        Assert.Equal(ExtractionState.Failed, image.State);
        Assert.Equal("engine returned no result", image.FailureReason);
    }

    [Fact]
    public void Re_run_a_failed_extraction()
    {
        var image = AnImage();
        image.TransitionTo(ExtractionState.Extracting);
        image.TransitionTo(ExtractionState.Failed, "engine returned no result");

        image.TransitionTo(ExtractionState.Pending);

        Assert.Equal(ExtractionState.Pending, image.State);
        Assert.Null(image.FailureReason);
    }

    [Theory]
    [InlineData(ExtractionState.Extracted)]
    [InlineData(ExtractionState.NeedsReview)]
    public void Re_run_a_completed_extraction(ExtractionState terminal)
    {
        var image = AnImage();
        image.TransitionTo(ExtractionState.Extracting);
        image.TransitionTo(terminal);

        image.TransitionTo(ExtractionState.Pending);

        Assert.Equal(ExtractionState.Pending, image.State);
    }

    [Theory]
    // Extraction must be entered before it can complete.
    [InlineData(ExtractionState.Pending, ExtractionState.Extracted)]
    [InlineData(ExtractionState.Pending, ExtractionState.NeedsReview)]
    [InlineData(ExtractionState.Pending, ExtractionState.Failed)]
    // A terminal state cannot slide into another terminal state without re-running.
    [InlineData(ExtractionState.Extracted, ExtractionState.NeedsReview)]
    [InlineData(ExtractionState.NeedsReview, ExtractionState.Extracted)]
    [InlineData(ExtractionState.Failed, ExtractionState.Extracted)]
    // Nothing re-enters Extracting except from Pending.
    [InlineData(ExtractionState.Extracted, ExtractionState.Extracting)]
    [InlineData(ExtractionState.Failed, ExtractionState.Extracting)]
    // Standing still is not a transition.
    [InlineData(ExtractionState.Pending, ExtractionState.Pending)]
    public void Invalid_transitions_are_rejected(ExtractionState from, ExtractionState to)
    {
        Assert.False(Receipt.IsTransitionAllowed(from, to));

        var image = AnImage();
        DriveTo(image, from);

        var error = Assert.Throws<InvalidOperationException>(() => image.TransitionTo(to));

        Assert.Contains("cannot move from", error.Message, StringComparison.Ordinal);
        Assert.Equal(from, image.State);
    }

    [Theory]
    [InlineData(ExtractionState.Pending, ExtractionState.Extracting)]
    [InlineData(ExtractionState.Extracting, ExtractionState.Extracted)]
    [InlineData(ExtractionState.Extracting, ExtractionState.NeedsReview)]
    [InlineData(ExtractionState.Extracting, ExtractionState.Failed)]
    [InlineData(ExtractionState.Extracted, ExtractionState.Pending)]
    [InlineData(ExtractionState.NeedsReview, ExtractionState.Pending)]
    [InlineData(ExtractionState.Failed, ExtractionState.Pending)]

    // A process that stopped mid-run leaves a receipt here, and the startup sweep returns it (D12).
    [InlineData(ExtractionState.Extracting, ExtractionState.Pending)]
    public void Allowed_transitions(ExtractionState from, ExtractionState to)
    {
        Assert.True(Receipt.IsTransitionAllowed(from, to));
    }

    [Fact]
    public void A_receipt_stranded_mid_extraction_can_be_returned_to_pending()
    {
        var image = AnImage();
        image.TransitionTo(ExtractionState.Extracting);

        image.TransitionTo(ExtractionState.Pending);

        Assert.Equal(ExtractionState.Pending, image.State);
    }

    [Fact]
    public void A_receipt_requires_the_storage_key_of_its_file()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Receipt.Of(new byte[32], "  ", "image/jpeg", sizeInBytes: 1));

        Assert.Equal("storageKey", error.ParamName);
    }

    [Fact]
    public void The_storage_key_is_retained_as_written()
    {
        // Stored rather than recomputed from the hash, so the layout of the store can change
        // without invalidating existing references (D11).
        Assert.Equal("ab/cd/abcd.jpg", AnImage().StorageKey);
    }

    private static void DriveTo(Receipt image, ExtractionState state)
    {
        if (state == ExtractionState.Pending)
        {
            return;
        }

        image.TransitionTo(ExtractionState.Extracting);
        if (state != ExtractionState.Extracting)
        {
            image.TransitionTo(state, state == ExtractionState.Failed ? "simulated" : null);
        }
    }
}
