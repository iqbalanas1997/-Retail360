using System.Reflection;
using Dapper;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Persistence.Sql;

public sealed class SqlMigrationRunner
{
    private const string MigrationsMarker = ".Migrations.";

    private readonly NpgsqlConnectionFactory _connections;

    public SqlMigrationRunner(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var scripts = LoadScripts();
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            create table if not exists schema_migrations (
                version text not null,
                applied_at timestamp with time zone not null,
                constraint pk_schema_migrations primary key (version)
            );
            """,
            cancellationToken: cancellationToken));

        var applied = (await connection.QueryAsync<string>(new CommandDefinition(
            "select version from schema_migrations;",
            cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);

        foreach (var script in scripts)
        {
            if (applied.Contains(script.Version))
            {
                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                script.Sql,
                transaction: transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into schema_migrations (version, applied_at)
                values (@Version, @AppliedAt);
                """,
                new { script.Version, AppliedAt = DateTimeOffset.UtcNow },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<SqlScript> LoadScripts()
    {
        var assembly = typeof(SqlMigrationRunner).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(MigrationsMarker, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new SqlScript(VersionFromResourceName(name), ReadResource(assembly, name)))
            .ToList();

        if (scripts.Count == 0)
        {
            throw new InvalidOperationException("No SQL migration scripts are embedded in Infrastructure.");
        }

        return scripts;
    }

    private static string VersionFromResourceName(string resourceName)
    {
        var markerIndex = resourceName.LastIndexOf(MigrationsMarker, StringComparison.Ordinal);
        var fileName = resourceName[(markerIndex + MigrationsMarker.Length)..];
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"SQL migration '{resourceName}' could not be read.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record SqlScript(string Version, string Sql);
}
