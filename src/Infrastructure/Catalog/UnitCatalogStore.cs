using Dapper;
using Retail360.Application.Catalog;
using Retail360.Domain.Catalog;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Catalog;

public sealed class UnitCatalogStore : IUnitStore
{
    private const string SelectColumns = """
        select id, name, created_at, updated_at, is_deleted
        from units
        """;

    private readonly NpgsqlConnectionFactory _connections;

    public UnitCatalogStore(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<Unit?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return await QueryOneAsync(
            $"{SelectColumns} where id = @Id and is_deleted = false;",
            new { Id = id },
            cancellationToken);
    }

    public async Task<Unit?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await QueryOneAsync(
            $"{SelectColumns} where is_deleted = false and lower(name) = lower(@Name);",
            new { Name = name },
            cancellationToken);
    }

    public async Task<IReadOnlyList<Unit>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<NamedRow>(new CommandDefinition(
            $"{SelectColumns} where is_deleted = false order by lower(name), id;",
            cancellationToken: cancellationToken));
        return rows.Select(ToUnit).ToArray();
    }

    public async Task<bool> IsReferencedByActiveProductAsync(Guid unitId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from products
                where is_deleted = false and unit_id = @Id
            );
            """,
            new { Id = unitId },
            cancellationToken: cancellationToken));
    }

    public Task AddAsync(Unit unit, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            insert into units (id, name, created_at, updated_at, is_deleted)
            values (@Id, @Name, @CreatedAt, @UpdatedAt, @IsDeleted);
            """,
            unit,
            "A unit with this name already exists.",
            requireRow: false,
            cancellationToken);
    }

    public Task UpdateAsync(Unit unit, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            update units
            set name = @Name,
                updated_at = @UpdatedAt,
                is_deleted = @IsDeleted
            where id = @Id and is_deleted = false;
            """,
            unit,
            "A unit with this name already exists.",
            requireRow: true,
            cancellationToken);
    }

    private async Task<Unit?> QueryOneAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<NamedRow>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        return row is null ? null : ToUnit(row);
    }

    private async Task ExecuteAsync(
        string sql,
        Unit unit,
        string conflictMessage,
        bool requireRow,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                unit,
                cancellationToken: cancellationToken));
            if (requireRow && rows == 0)
            {
                throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Unit was not found.");
            }
        }
        catch (CatalogRuleException)
        {
            throw;
        }
        catch (Exception exception) when (PostgresUnique.AsConflict(exception, conflictMessage) is { } conflict)
        {
            throw conflict;
        }
    }

    private static Unit ToUnit(NamedRow row)
    {
        return Unit.Restore(row.Id, row.Name, row.CreatedAt, row.UpdatedAt, row.IsDeleted);
    }

    private sealed class NamedRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }
}
