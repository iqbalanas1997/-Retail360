namespace Retail360.Tests;

public static class PurchaseDatabaseGuard
{
    public const string PermittedDatabase = "retail360_purchasing_test";

    public static void Require(string? databaseName)
    {
        if (databaseName is "retail360"
            or "retail360_dev"
            or "retail360_catalog_test"
            or "retail360_inventory_test"
            or "retail360_identity_test"
            or "retail360_suppliers_test")
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated purchasing test database.");
        }

        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated purchasing test database.");
        }
    }
}
