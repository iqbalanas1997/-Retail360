namespace Retail360.Tests;

public static class IdentityDatabaseGuard
{
    public const string PermittedDatabase = "retail360_identity_test";

    public static void Require(string? databaseName)
    {
        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated identity test database.");
        }
    }
}
