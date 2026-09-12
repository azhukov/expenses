namespace Expenses.Integration.Tests.Harness;

/// <summary>
/// One container for the whole suite. Starting a database per test class would multiply a
/// multi-second cost by every class for no isolation the schema does not already give.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
