namespace Expenses.Application.Dtos;

/// <summary>
/// How an incoming merchant was reconciled with the dictionary, reported rather than inferred:
/// matching by tax number is a fact, matching by name is a guess, and creating one is neither (D18).
/// </summary>
public enum MerchantMatchKind
{
    MatchedByTaxId = 0,
    MatchedByName = 1,
    Created = 2,
}
