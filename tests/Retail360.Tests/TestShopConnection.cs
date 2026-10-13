using System.Text.Json;
using Npgsql;

namespace Retail360.Tests;

internal static class TestShopConnection
{
    public static string Read()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var connectionString = string.IsNullOrWhiteSpace(fromEnvironment)
            ? ReadAppsettings()
            : fromEnvironment;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var password = Environment.GetEnvironmentVariable("RETAIL360_DB_PASSWORD");
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        if (string.IsNullOrEmpty(builder.Password))
        {
            throw new InvalidOperationException(
                "Database password is not configured. Set RETAIL360_DB_PASSWORD. The value is not written to the log.");
        }

        return builder.ConnectionString;
    }

    private static string ReadAppsettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Retail360.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Retail360.sln was not found above the test output directory.");
        }

        var path = Path.Combine(directory.FullName, "src", "Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var connectionString = document.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection")
            .GetString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection is missing from appsettings.json.");
        }

        return connectionString;
    }
}
