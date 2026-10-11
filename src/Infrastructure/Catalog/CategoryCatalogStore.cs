using Dapper;
using Retail360.Application.Catalog;
using Retail360.Domain.Catalog;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Catalog;

public sealed class CategoryCatalogStore : ICategoryStore
{
    private const string SelectColumns = """
        select id, name, created_at, updated_at, is_deleted
        from categories
        """;

    private readonly NpgsqlConnectionFactory _connections;

    public CategoryCatalogStore(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return await QueryOneAsync(
            $"{SelectColumns} where id = @Id and is_deleted = false;",
            new { Id = id },
            cancellationToken);
    }

    public async Task<Category?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await QueryOneAsync(
            $"{SelectColumns} where is_deleted = false and lower(name) = lower(@Name);",
            new { Name = name },
            cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<NamedRow>(new CommandDefinition(
            $"{SelectColumns} where is_deleted = false order by lower(name), id;",
            cancellationToken: cancellationToken));
        return rows.Select(ToCategory).ToArray();
    }

    public async Task<bool> IsReferencedByActiveProductAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from products
                where is_deleted = false and category_id = @Id
            );
            """,
            new { Id = categoryId },
            cancellationToken: cancellationToken));
    }

    public Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            insert into categories (id, name, created_at, updated_at, is_deleted)
            values (@Id, @Name, @CreatedAt, @UpdatedAt, @IsDeleted);
            """,
            category,
            "A category with this name already exists.",
            requireRow: false,
            cancellationToken);
    }

    public Task UpdateAsync(Category category, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            update categories
            set name = @Name,
                updated_at = @UpdatedAt,
                is_deleted = @IsDeleted
            where id = @Id and is_deleted = false;
            """,
            category,
            "A category with this name already exists.",
            requireRow: true,
            cancellationToken);
    }

    private async Task<Category?> QueryOneAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<NamedRow>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        return row is null ? null : ToCategory(row);
    }

    private async Task ExecuteAsync(
        string sql,
        Category category,
        string conflictMessage,
        bool requireRow,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                category,
                cancellationToken: cancellationToken));
            if (requireRow && rows == 0)
            {
                throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Category was not found.");
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

    private static Category ToCategory(NamedRow row)
    {
        return Category.Restore(row.Id, row.Name, row.CreatedAt, row.UpdatedAt, row.IsDeleted);
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
