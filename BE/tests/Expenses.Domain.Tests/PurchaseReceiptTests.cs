using Expenses.Domain.Entities;
using ExtractionState = Expenses.Domain.Entities.Purchase.ExtractionState;
using FiscalSource = Expenses.Domain.Entities.FiscalInvoice.FiscalSource;

namespace Expenses.Domain.Tests;

/// <summary>
/// What a purchase carries of its receipt — an image, a fiscal invoice, both or neither — and the
/// extraction state that exists exactly when it carries either (D35). Scenarios from
/// receipt-ingestion, "Fiscal identity belongs to the purchase, not to the image": "A purchase with a
/// fiscal identity and no image", "A purchase with both", "Deleting the image keeps the fiscal
/// identity"; and from "Extraction lifecycle": "No state without a receipt".
/// </summary>
public sealed class PurchaseReceiptTests
{
    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65";

    private static readonly DateTime s_occurred = new(2026, 8, 29, 14, 59, 22, DateTimeKind.Unspecified);

    private static Receipt AnImage() => Receipt.Of("ab/cd/abcd.jpg", "image/jpeg", sizeInBytes: 2_000_000);

    private static FiscalInvoice AnInvoice()
    {
        var invoice = FiscalInvoice.Create();
        invoice.RecordPayload(Payload, FiscalSource.SuppliedAtUpload);
        invoice.SupplyIdentifiers("32AA324CFF5030271E16D59F7F8EF636", jikr: null);

        return invoice;
    }

    private static Purchase Captured(
        Receipt? receipt,
        FiscalInvoice? fiscal,
        ExtractionState? extraction = ExtractionState.Extracted,
        string? failureReason = null)
        => Purchase.Record(
            s_occurred,
            amount: 59.65m,
            [Expense.Record("Groceries", amount: 59.65m)],
            receipt: receipt,
            fiscal: fiscal,
            extraction: extraction,
            extractionFailureReason: failureReason);

    [Fact]
    public void A_purchase_with_a_fiscal_identity_and_no_image()
    {
        var invoice = AnInvoice();

        var purchase = Captured(receipt: null, invoice);

        Assert.Same(invoice, purchase.Fiscal);
        Assert.Null(purchase.Receipt);
        Assert.Equal(ExtractionState.Extracted, purchase.Extraction);
    }

    [Fact]
    public void A_purchase_with_both()
    {
        var image = AnImage();
        var invoice = AnInvoice();

        var purchase = Captured(image, invoice);

        Assert.Same(image, purchase.Receipt);
        Assert.Same(invoice, purchase.Fiscal);
    }

    [Fact]
    public void Deleting_the_image_keeps_the_fiscal_identity()
    {
        var invoice = AnInvoice();
        var purchase = Captured(AnImage(), invoice, ExtractionState.NeedsReview);

        purchase.DetachReceipt();

        Assert.Null(purchase.Receipt);
        Assert.Same(invoice, purchase.Fiscal);
        Assert.Equal(ExtractionState.NeedsReview, purchase.Extraction);
    }

    [Fact]
    public void Deleting_the_only_receipt_takes_the_extraction_state_with_it()
    {
        var purchase = Captured(AnImage(), fiscal: null, ExtractionState.Failed, "no result");

        purchase.DetachReceipt();

        // Nothing is left to have been extracted, so a state would describe nothing.
        Assert.Null(purchase.Extraction);
        Assert.Null(purchase.ExtractionFailureReason);
    }

    [Fact]
    public void No_state_without_a_receipt()
    {
        var purchase = Purchase.Record(s_occurred, amount: 12.40m, [Expense.Record("Lunch", amount: 12.40m)]);

        Assert.Null(purchase.Receipt);
        Assert.Null(purchase.Fiscal);
        Assert.Null(purchase.Extraction);
    }

    [Fact]
    public void A_receipt_without_an_extraction_state_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Captured(AnImage(), fiscal: null, extraction: null));
    }

    [Fact]
    public void A_fiscal_invoice_without_an_extraction_state_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Captured(receipt: null, AnInvoice(), extraction: null));
    }

    [Fact]
    public void An_extraction_state_with_nothing_extracted_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Captured(receipt: null, fiscal: null));
    }

    [Fact]
    public void An_empty_fiscal_invoice_is_no_fiscal_invoice()
    {
        // An invoice nothing was ever recorded on would persist as a row claiming a fiscal identity
        // that holds no value at all (D36).
        var purchase = Captured(AnImage(), FiscalInvoice.Create());

        Assert.Null(purchase.Fiscal);
    }

    [Fact]
    public void A_failure_reason_is_kept_only_for_a_failed_extraction()
    {
        Assert.Equal("no result", Captured(AnImage(), null, ExtractionState.Failed, "no result").ExtractionFailureReason);
        Assert.Null(Captured(AnImage(), null, ExtractionState.Extracted, "ignored").ExtractionFailureReason);
    }

    [Theory]
    [InlineData(ExtractionState.Extracted, ExtractionState.NeedsReview)]
    [InlineData(ExtractionState.NeedsReview, ExtractionState.Extracted)]
    [InlineData(ExtractionState.Failed, ExtractionState.Extracted)]
    [InlineData(ExtractionState.Extracted, ExtractionState.Failed)]
    public void Re_running_moves_directly_between_terminal_states(ExtractionState from, ExtractionState to)
    {
        var purchase = Captured(receipt: null, AnInvoice(), from, from == ExtractionState.Failed ? "first attempt" : null);

        purchase.TransitionExtraction(to, to == ExtractionState.Failed ? "second attempt" : null);

        Assert.Equal(to, purchase.Extraction);
        Assert.Equal(to == ExtractionState.Failed ? "second attempt" : null, purchase.ExtractionFailureReason);
    }

    [Fact]
    public void Re_running_a_purchase_with_nothing_to_extract_is_refused()
    {
        var purchase = Purchase.Record(s_occurred, amount: 12.40m, [Expense.Record("Lunch", amount: 12.40m)]);

        Assert.Throws<InvalidOperationException>(() => purchase.TransitionExtraction(ExtractionState.Extracted));
    }

    [Fact]
    public void A_re_run_can_give_an_imaged_purchase_the_invoice_its_code_decoded_to()
    {
        var purchase = Captured(AnImage(), fiscal: null);
        var decoded = FiscalInvoice.Create();
        decoded.RecordPayload(Payload, FiscalSource.DecodedFromCode);

        purchase.AttachFiscalInvoice(decoded);

        Assert.Same(decoded, purchase.Fiscal);
    }

    [Fact]
    public void An_empty_invoice_is_not_attached()
    {
        var purchase = Captured(AnImage(), fiscal: null);

        purchase.AttachFiscalInvoice(FiscalInvoice.Create());

        Assert.Null(purchase.Fiscal);
    }

    [Fact]
    public void A_manual_purchase_does_not_gain_a_fiscal_invoice()
    {
        // A purchase acquires what it was read from when it is created; only one that was read from
        // an image can learn, on a re-run, what that image's code said.
        var purchase = Purchase.Record(s_occurred, amount: 12.40m, [Expense.Record("Lunch", amount: 12.40m)]);

        Assert.Throws<InvalidOperationException>(() => purchase.AttachFiscalInvoice(AnInvoice()));
    }

    [Fact]
    public void A_purchase_that_already_has_an_invoice_keeps_it()
    {
        var invoice = AnInvoice();
        var purchase = Captured(AnImage(), invoice);

        Assert.Throws<InvalidOperationException>(() => purchase.AttachFiscalInvoice(AnInvoice()));
        Assert.Same(invoice, purchase.Fiscal);
    }
}
