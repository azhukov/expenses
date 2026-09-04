using Expenses.Application.Errors;
using Expenses.Application.Merchants;
using Expenses.Application.Tests.Fakes;
using Expenses.Domain;

namespace Expenses.Application.Tests.Merchants;

/// <summary>
/// Scenarios from reference-data: "Merchants are a dictionary learned during ingestion",
/// "A merchant is identified by its tax identification number where one exists", "A merchant may
/// belong to a parent merchant", "Merchants are retired, not deleted", "Merchants can be listed
/// and searched".
/// </summary>
public sealed class MerchantUseCaseTests
{
    private readonly InMemoryLedger _ledger = new();

    private ResolveMerchant Resolve => new(_ledger, _ledger);

    [Fact]
    public async Task An_unknown_merchant_is_added()
    {
        var resolution = await Resolve.Execute("AROMA", "02440261");
        await _ledger.SaveChanges();

        Assert.Equal(MerchantMatchKind.Created, resolution.Kind);
        Assert.True(resolution.NewlyAdded);
        Assert.Equal("AROMA", Assert.Single(_ledger.Merchants).Name);
    }

    [Fact]
    public async Task Same_tax_number_is_the_same_merchant()
    {
        _ledger.Given(Merchant.Create("AROMA", "02440261"));

        var resolution = await Resolve.Execute("DOMACA TRGOVINA doo", "02440261");

        Assert.Equal(MerchantMatchKind.MatchedByTaxId, resolution.Kind);
        Assert.Single(_ledger.Merchants);

        // Matching by tax number does not rewrite the dictionary name from the receipt.
        Assert.Equal("AROMA", resolution.Merchant.Name);
    }

    [Fact]
    public async Task Different_tax_numbers_are_different_merchants()
    {
        _ledger.Given(Merchant.Create("MARKET", "02440261"));

        var resolution = await Resolve.Execute("MARKET", "03001234");
        await _ledger.SaveChanges();

        Assert.Equal(MerchantMatchKind.Created, resolution.Kind);
        Assert.Equal(2, _ledger.Merchants.Count);
    }

    [Fact]
    public async Task Merchant_without_a_tax_number_is_matched_by_name()
    {
        _ledger.Given(Merchant.Create("Pijaca Stall 12"));

        var resolution = await Resolve.Execute("Pijaca Stall 12");

        Assert.Equal(MerchantMatchKind.MatchedByName, resolution.Kind);
        Assert.Single(_ledger.Merchants);
    }

    [Fact]
    public async Task A_name_match_is_not_used_when_a_tax_number_was_printed()
    {
        // The name on the paper is a brand and is what OCR mangles; the tax number is the identity
        // (D18). A name-only entry must not absorb a receipt that carried a tax number.
        _ledger.Given(Merchant.Create("AROMA"));

        var resolution = await Resolve.Execute("AROMA", "02440261");
        await _ledger.SaveChanges();

        Assert.Equal(MerchantMatchKind.Created, resolution.Kind);
        Assert.Equal(2, _ledger.Merchants.Count);
    }

    [Fact]
    public async Task A_merchant_can_be_renamed()
    {
        var merchant = _ledger.Given(Merchant.Create("AROMA d.o.o."));

        var renamed = await new RenameMerchant(_ledger, _ledger).Execute(merchant.Id, "AROMA");

        Assert.Equal("AROMA", renamed.Name);
        Assert.Equal(merchant.Id, renamed.Id);
    }

    [Fact]
    public async Task Branch_beneath_a_chain()
    {
        var chain = _ledger.Given(Merchant.Create("AROMA", "02440261"));
        var branch = _ledger.Given(Merchant.Create("Aroma 034"));

        var updated = await new SetMerchantParent(_ledger, _ledger).Execute(branch.Id, chain.Id);

        Assert.Equal(chain.Id, updated.ParentId);
        Assert.Equal("Aroma 034", Assert.Single(chain.Children).Name);
    }

    [Fact]
    public async Task Cycle_rejected()
    {
        var chain = _ledger.Given(Merchant.Create("AROMA"));
        var branch = _ledger.Given(Merchant.Create("Aroma 034"));
        await new SetMerchantParent(_ledger, _ledger).Execute(branch.Id, chain.Id);

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            new SetMerchantParent(_ledger, _ledger).Execute(chain.Id, branch.Id));

        Assert.Equal(ApplicationErrors.MerchantParentCycle, error.Error.Code);
    }

    [Fact]
    public async Task Merchant_parent_with_active_children()
    {
        var chain = _ledger.Given(Merchant.Create("AROMA"));
        var branch = _ledger.Given(Merchant.Create("Aroma 034"));
        await new SetMerchantParent(_ledger, _ledger).Execute(branch.Id, chain.Id);

        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            new DeactivateMerchant(_ledger, _ledger).Execute(chain.Id));

        Assert.Equal(ApplicationErrors.MerchantHasActiveChildren, error.Error.Code);
        Assert.Equal("Aroma 034", Assert.IsType<IEnumerable<string>>(error.Error.Fields["children"], exactMatch: false).Single());
        Assert.True(chain.IsActive);
    }

    [Fact]
    public async Task Deactivated_merchant_is_hidden_from_selection()
    {
        var voli = _ledger.Given(Merchant.Create("VOLI"));
        _ledger.Given(Merchant.Create("AROMA"));

        await new DeactivateMerchant(_ledger, _ledger).Execute(voli.Id);
        var offered = await new ListMerchants(_ledger).Execute();

        Assert.Equal(["AROMA"], offered.Select(merchant => merchant.Name));
        Assert.Equal(2, (await new ListMerchants(_ledger).Execute(includeInactive: true)).Count);
    }

    [Fact]
    public async Task Search_finds_a_dictionary_merchant()
    {
        _ledger.Given(Merchant.Create("AROMA"));

        var matches = await new SearchMerchants(_ledger).Execute("arom");

        Assert.Equal("AROMA", Assert.Single(matches).Merchant?.Name);
    }

    [Fact]
    public async Task Search_finds_an_unmatched_merchant()
    {
        _ledger.Given(Purchase.Record(
            new DateTime(2026, 8, 19, 14, 3, 0, DateTimeKind.Unspecified),
            8.48m,
            [Expense.Record("Groceries", 8.48m)],
            merchantRaw: "PIJACA STALL 12"));

        var matches = await new SearchMerchants(_ledger).Execute("stall");

        var match = Assert.Single(matches);
        Assert.Null(match.Merchant);
        Assert.Equal("PIJACA STALL 12", match.MatchedText);
    }

    [Fact]
    public async Task Searching_for_nothing_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            new SearchMerchants(_ledger).Execute("  "));

        Assert.Equal(ApplicationErrors.MerchantSearchTermRequired, error.Error.Code);
    }

    [Fact]
    public async Task Renaming_a_merchant_that_does_not_exist_is_reported()
    {
        var error = await Assert.ThrowsAsync<ExpensesException>(() =>
            new RenameMerchant(_ledger, _ledger).Execute(4711, "AROMA"));

        Assert.Equal(ApplicationErrors.MerchantNotFound, error.Error.Code);
    }
}
