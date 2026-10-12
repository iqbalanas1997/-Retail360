namespace Retail360.Tests;

public static class CatalogDatabaseGuard
{
    public const string PermittedDatabase = "retail360_catalog_test";

    public static void Require(string? databaseName)
    {
        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated catalog test database.");
        }
    }
}
