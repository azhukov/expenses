namespace Expenses.Domain.Tests;

/// <summary>
/// Scenarios from reference-data: "Merchants are a dictionary learned during ingestion",
/// "A merchant is identified by its tax identification number where one exists",
/// "A merchant may belong to a parent merchant", "Merchants are retired, not deleted".
/// </summary>
public sealed class MerchantTests
{
    [Fact]
    public void Merchant_without_a_tax_number_is_valid()
    {
        var stall = Merchant.Create("Pijaca");

        Assert.Null(stall.TaxId);
        Assert.True(stall.IsActive);
    }

    [Fact]
    public void Merchant_carries_its_tax_identification_number()
    {
        var aroma = Merchant.Create("AROMA", taxId: "02440261");

        Assert.Equal("02440261", aroma.TaxId);
    }

    [Fact]
    public void Branch_beneath_a_chain()
    {
        var chain = Merchant.Create("AROMA", taxId: "02440261");

        var branch = Merchant.Create("Aroma 034", parent: chain);

        Assert.Same(chain, branch.Parent);
        Assert.Contains(branch, chain.Children);
    }

    [Fact]
    public void Cycle_rejected_when_a_merchant_is_made_its_own_parent()
    {
        var aroma = Merchant.Create("AROMA");

        var error = Assert.Throws<InvalidOperationException>(() => aroma.SetParent(aroma));

        Assert.Contains("cycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cycle_rejected_when_a_merchant_is_made_a_descendant_of_itself()
    {
        var chain = Merchant.Create("AROMA");
        var branch = Merchant.Create("Aroma 034", parent: chain);
        var till = Merchant.Create("Aroma 034 kasa 2", parent: branch);

        var error = Assert.Throws<InvalidOperationException>(() => chain.SetParent(till));

        Assert.Contains("cycle", error.Message, StringComparison.Ordinal);
        Assert.Null(chain.Parent);
    }

    [Fact]
    public void A_merchant_can_be_renamed()
    {
        var merchant = Merchant.Create("AR0MA", taxId: "02440261");

        merchant.Rename("AROMA");

        Assert.Equal("AROMA", merchant.Name);
        Assert.Equal("02440261", merchant.TaxId);
    }

    [Fact]
    public void Name_is_required()
    {
        var error = Assert.Throws<ArgumentException>(() => Merchant.Create(" "));

        Assert.Equal("name", error.ParamName);
    }

    [Fact]
    public void Deactivated_merchant_is_marked_inactive()
    {
        var merchant = Merchant.Create("AROMA");

        merchant.Deactivate();

        Assert.False(merchant.IsActive);
    }

    [Fact]
    public void Merchants_are_never_system_provided()
    {
        // D18: no merchant ships with the product, so there is nothing for is_system to protect.
        Assert.DoesNotContain(
            typeof(Merchant).GetProperties(),
            property => property.Name is "IsSystem" or "Code");
    }
}
