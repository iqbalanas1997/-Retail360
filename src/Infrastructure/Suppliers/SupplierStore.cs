using Dapper;
using Npgsql;
using Retail360.Application.Suppliers;
using Retail360.Domain.Suppliers;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Suppliers;

public sealed class SupplierStore : ISupplierStore
{
    private const string SelectColumns = """
        select id, name, contact_person, phone, email, address, notes, is_active, created_at, updated_at, is_deleted
        from suppliers
        """;

    private readonly NpgsqlConnectionFactory _connections;

    public SupplierStore(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<Supplier?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return await QueryOneAsync(
            $"{SelectColumns} where id = @Id and is_deleted = false;",
            new { Id = id },
            cancellationToken);
    }

    public async Task<Supplier?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await QueryOneAsync(
            $"{SelectColumns} where is_deleted = false and lower(name) = lower(@Name);",
            new { Name = name },
            cancellationToken);
    }

    public async Task<IReadOnlyList<Supplier>> ListAsync(
        string? name,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<SupplierRow>(new CommandDefinition(
            $"""
            {SelectColumns}
            where is_deleted = false
              and (@ActiveOnly = false or is_active = true)
              and (@NamePattern is null or name ilike @NamePattern escape '\')
            order by lower(name), id
            limit @Limit offset @Offset;
            """,
            new
            {
                ActiveOnly = activeOnly,
                NamePattern = ContainsPattern(name),
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken));
        return rows.Select(ToSupplier).ToArray();
    }

    public Task AddAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            insert into suppliers (
                id, name, contact_person, phone, email, address, notes, is_active,
                created_at, updated_at, is_deleted)
            values (
                @Id, @Name, @ContactPerson, @Phone, @Email, @Address, @Notes, @IsActive,
                @CreatedAt, @UpdatedAt, @IsDeleted);
            """,
            supplier,
            requireRow: false,
            cancellationToken);
    }

    public Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            update suppliers
            set name = @Name,
                contact_person = @ContactPerson,
                phone = @Phone,
                email = @Email,
                address = @Address,
                notes = @Notes,
                is_active = @IsActive,
                updated_at = @UpdatedAt,
                is_deleted = @IsDeleted
            where id = @Id and is_deleted = false;
            """,
            supplier,
            requireRow: true,
            cancellationToken);
    }

    private async Task<Supplier?> QueryOneAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SupplierRow>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        return row is null ? null : ToSupplier(row);
    }

    private async Task ExecuteAsync(
        string sql,
        Supplier supplier,
        bool requireRow,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                supplier,
                cancellationToken: cancellationToken));
            if (requireRow && rows == 0)
            {
                throw new SupplierRuleException(SupplierRuleFailure.NotFound, "Supplier was not found.");
            }
        }
        catch (SupplierRuleException)
        {
            throw;
        }
        catch (Exception exception) when (UniqueConflict(exception) is { } conflict)
        {
            throw conflict;
        }
    }

    private static SupplierRuleException? UniqueConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return new SupplierRuleException(
                    SupplierRuleFailure.Conflict,
                    "A supplier with this name already exists.");
            }
        }

        return null;
    }

    private static string? ContainsPattern(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var escaped = name.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static Supplier ToSupplier(SupplierRow row)
    {
        return Supplier.Restore(
            row.Id,
            row.Name,
            row.ContactPerson,
            row.Phone,
            row.Email,
            row.Address,
            row.Notes,
            row.IsActive,
            row.CreatedAt,
            row.UpdatedAt,
            row.IsDeleted);
    }

    private sealed class SupplierRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? ContactPerson { get; init; }

        public string? Phone { get; init; }

        public string? Email { get; init; }

        public string? Address { get; init; }

        public string? Notes { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }
}
