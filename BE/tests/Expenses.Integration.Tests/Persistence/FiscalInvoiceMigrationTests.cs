using Expenses.Domain.Entities;
using Expenses.Infrastructure.Persistence;
using Expenses.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ExtractionState = Expenses.Domain.Entities.Purchase.ExtractionState;
using FiscalSource = Expenses.Domain.Entities.FiscalInvoice.FiscalSource;

namespace Expenses.Integration.Tests.Persistence;

/// <summary>
/// The fiscal invoice moving off the receipt image and onto the purchase (D35, D36). Scenario from
/// receipt-ingestion, "Fiscal identity belongs to the purchase, not to the image": "Existing receipts
/// keep their fiscal identity"; and the round trip of "A purchase with a fiscal identity and no image".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalInvoiceMigrationTests(PostgresFixture postgres)
{
    /// <summary>The last migration in which the fiscal columns belonged to the receipt image.</summary>
    private const string BeforeTheSplit = "20260910165733_AddReceiptFiscalPayload";

    private const string Payload =
        "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
        + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65";

    [Fact]
    public async Task Existing_receipts_keep_their_fiscal_identity()
    {
        string connection = await postgres.ProvisionAnother("fiscal_invoice_split");

        await using (var before = Context(connection))
        {
            await before.GetService<IMigrator>().MigrateAsync(BeforeTheSplit);
        }

        // Three rows as the old shape wrote them: an image that carried a fiscal code, an image that
        // carried none — whose source columns the old mapping still wrote as zero — and a manual
        // entry with no receipt at all.
        await Execute(connection, $"""
            INSERT INTO purchases (occurred_at, amount, receipt_storage_key, receipt_content_type,
                receipt_size_in_bytes, receipt_state, fiscal_ikof_supplied, fiscal_jikr_extracted,
                fiscal_extracted_source, fiscal_payload, fiscal_payload_source)
            VALUES ('2026-08-29 14:59:22', 59.65, 'aa/bb/with-code.jpg', 'image/jpeg', 2000000, 3,
                '32AA324CFF5030271E16D59F7F8EF636', 'a1b2c3d4-0000-0000-0000-000000000000', 4,
                '{Payload}', 1);

            INSERT INTO purchases (occurred_at, amount, receipt_storage_key, receipt_content_type,
                receipt_size_in_bytes, receipt_state, receipt_failure_reason, fiscal_extracted_source,
                fiscal_payload_source)
            VALUES ('2026-08-30 10:00:00', 12.40, 'cc/dd/no-code.jpg', 'image/png', 1000, 4,
                'no result', 0, 0);

            INSERT INTO purchases (occurred_at, amount) VALUES ('2026-08-31 09:00:00', 3.20);
            """);

        await using (var after = Context(connection))
        {
            await after.Database.MigrateAsync();
        }

        await using var read = Context(connection);
        var purchases = await read.Purchases.OrderBy(purchase => purchase.OccurredAt).ToListAsync();

        var withCode = purchases[0];
        Assert.Equal("aa/bb/with-code.jpg", withCode.Receipt?.StorageKey);
        Assert.Equal(ExtractionState.NeedsReview, withCode.Extraction);
        Assert.NotNull(withCode.Fiscal);
        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", withCode.Fiscal.FiscalIkofSupplied);
        Assert.Equal("a1b2c3d4-0000-0000-0000-000000000000", withCode.Fiscal.FiscalJikrExtracted);
        Assert.Equal(FiscalSource.RetrievedFromService, withCode.Fiscal.FiscalExtractedSource);
        Assert.Equal(Payload, withCode.Fiscal.FiscalPayload);
        Assert.Equal(FiscalSource.SuppliedAtUpload, withCode.Fiscal.FiscalPayloadSource);

        var withoutCode = purchases[1];
        Assert.Equal("cc/dd/no-code.jpg", withoutCode.Receipt?.StorageKey);
        Assert.Equal(ExtractionState.Failed, withoutCode.Extraction);
        Assert.Equal("no result", withoutCode.ExtractionFailureReason);
        Assert.Null(withoutCode.Fiscal);

        var manual = purchases[2];
        Assert.Null(manual.Receipt);
        Assert.Null(manual.Fiscal);
        Assert.Null(manual.Extraction);
    }

    [Fact]
    public async Task A_purchase_with_a_fiscal_identity_and_no_image_round_trips()
    {
        await postgres.Migrate();

        var fiscal = FiscalInvoice.Create();
        fiscal.RecordPayload(Payload, FiscalSource.SuppliedAtUpload);
        fiscal.SupplyIdentifiers("32AA324CFF5030271E16D59F7F8EF636", jikr: null);
        fiscal.RecordExtractedIdentifiers(ikof: null, "a1b2c3d4-0000-0000-0000-000000000000", FiscalSource.RetrievedFromService);

        long id;
        await using (var write = postgres.Context())
        {
            var purchase = Purchase.Record(
                new DateTime(2026, 7, 1, 11, 22, 33, DateTimeKind.Unspecified),
                amount: 59.65m,
                [Expense.Record("Groceries", amount: 59.65m, unitId: 1)],
                fiscal: fiscal,
                extraction: ExtractionState.Extracted);

            write.Purchases.Add(purchase);
            await write.SaveChangesAsync();
            id = purchase.Id;
        }

        await using var read = postgres.Context();
        var stored = await read.Purchases.SingleAsync(purchase => purchase.Id == id);

        Assert.Null(stored.Receipt);
        Assert.Equal(ExtractionState.Extracted, stored.Extraction);
        Assert.NotNull(stored.Fiscal);
        Assert.Equal(Payload, stored.Fiscal.FiscalPayload);
        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", stored.Fiscal.FiscalIkofSupplied);
        Assert.Equal("a1b2c3d4-0000-0000-0000-000000000000", stored.Fiscal.FiscalJikrExtracted);
        Assert.Equal(FiscalSource.RetrievedFromService, stored.Fiscal.FiscalExtractedSource);
    }

    private static ExpensesDbContext Context(string connection)
        => new(new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseNpgsql(connection)
            .Options);

    private static async Task Execute(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync();
    }
}
