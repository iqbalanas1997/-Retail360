using Dapper;
using Retail360.Application.Catalog;
using Retail360.Domain.Catalog;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Catalog;

public sealed class ProductCatalogStore : IProductStore
{
    private const string ProductColumns = """
        p.id,
        p.name,
        p.category_id,
        c.name as category_name,
        p.brand_id,
        b.name as brand_name,
        p.unit_id,
        u.name as unit_name,
        p.purchase_price,
        p.selling_price,
        p.minimum_stock,
        p.is_active,
        p.created_at,
        p.updated_at
        """;

    private const string ProductJoins = """
        from products p
        join units u on u.id = p.unit_id
        left join categories c on c.id = p.category_id and c.is_deleted = false
        left join brands b on b.id = p.brand_id and b.is_deleted = false
        """;

    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public ProductCatalogStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<Product?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProductWriteRow>(new CommandDefinition(
            """
            select
                id,
                name,
                category_id,
                brand_id,
                unit_id,
                purchase_price,
                selling_price,
                minimum_stock,
                is_active,
                created_at,
                updated_at,
                is_deleted
            from products
            where id = @Id and is_deleted = false;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var barcodes = await connection.QueryAsync<BarcodeWriteRow>(new CommandDefinition(
            """
            select id, product_id, barcode, created_at, updated_at, is_deleted
            from product_barcodes
            where product_id = @Id and is_deleted = false
            order by barcode;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        return Product.Restore(
            row.Id,
            row.Name,
            row.CategoryId,
            row.BrandId,
            row.UnitId,
            row.PurchasePrice,
            row.SellingPrice,
            row.MinimumStock,
            row.IsActive,
            row.CreatedAt,
            row.UpdatedAt,
            row.IsDeleted,
            barcodes.Select(barcode => ProductBarcode.Restore(
                barcode.Id,
                barcode.ProductId,
                barcode.Barcode,
                barcode.CreatedAt,
                barcode.UpdatedAt,
                barcode.IsDeleted)));
    }

    public async Task<ProductResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProductReadRow>(new CommandDefinition(
            $"""
            select {ProductColumns}
            {ProductJoins}
            where p.id = @Id and p.is_deleted = false;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var barcodes = await LoadBarcodesAsync(connection, [row.Id], cancellationToken);
        return ToResponse(row, barcodes.GetValueOrDefault(row.Id) ?? []);
    }

    public async Task<ProductResponse?> FindActiveByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProductReadRow>(new CommandDefinition(
            $"""
            select {ProductColumns}
            {ProductJoins}
            join product_barcodes pb on pb.product_id = p.id
            where pb.is_deleted = false
              and p.is_deleted = false
              and p.is_active = true
              and pb.barcode = @Barcode;
            """,
            new { Barcode = barcode },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var barcodes = await LoadBarcodesAsync(connection, [row.Id], cancellationToken);
        return ToResponse(row, barcodes.GetValueOrDefault(row.Id) ?? []);
    }

    public async Task<IReadOnlyList<ProductResponse>> ListAsync(
        string? name,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<ProductReadRow>(new CommandDefinition(
            $"""
            select {ProductColumns}
            {ProductJoins}
            where p.is_deleted = false
              and (@ActiveOnly = false or p.is_active = true)
              and (@NamePattern is null or p.name ilike @NamePattern escape '\')
            order by lower(p.name), p.id
            limit @Limit offset @Offset;
            """,
            new
            {
                ActiveOnly = activeOnly,
                NamePattern = ContainsPattern(name),
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken))).ToArray();
        if (rows.Length == 0)
        {
            return [];
        }

        var barcodes = await LoadBarcodesAsync(connection, rows.Select(row => row.Id).ToArray(), cancellationToken);
        return rows
            .Select(row => ToResponse(row, barcodes.GetValueOrDefault(row.Id) ?? []))
            .ToArray();
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        try
        {
            await _transactions.ExecuteAsync(async (connection, transaction, token) =>
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into products (
                        id, name, category_id, brand_id, unit_id,
                        purchase_price, selling_price, minimum_stock, is_active,
                        created_at, updated_at, is_deleted)
                    values (
                        @Id, @Name, @CategoryId, @BrandId, @UnitId,
                        @PurchasePrice, @SellingPrice, @MinimumStock, @IsActive,
                        @CreatedAt, @UpdatedAt, @IsDeleted);
                    """,
                    product,
                    transaction,
                    cancellationToken: token));
                await InsertBarcodesAsync(connection, transaction, product, token);
            }, cancellationToken);
        }
        catch (CatalogRuleException)
        {
            throw;
        }
        catch (Exception exception) when (PostgresUnique.AsConflict(exception, "Barcode is already assigned to a product.") is { } conflict)
        {
            throw conflict;
        }
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken)
    {
        try
        {
            await _transactions.ExecuteAsync(async (connection, transaction, token) =>
            {
                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    update products
                    set name = @Name,
                        category_id = @CategoryId,
                        brand_id = @BrandId,
                        unit_id = @UnitId,
                        purchase_price = @PurchasePrice,
                        selling_price = @SellingPrice,
                        minimum_stock = @MinimumStock,
                        is_active = @IsActive,
                        updated_at = @UpdatedAt,
                        is_deleted = @IsDeleted
                    where id = @Id and is_deleted = false;
                    """,
                    product,
                    transaction,
                    cancellationToken: token));
                if (rows == 0)
                {
                    throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Product was not found.");
                }

                var deleted = product.Barcodes.Where(barcode => barcode.IsDeleted).ToArray();
                if (deleted.Length > 0)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        update product_barcodes
                        set is_deleted = true,
                            updated_at = @UpdatedAt
                        where id = @Id and is_deleted = false;
                        """,
                        deleted,
                        transaction,
                        cancellationToken: token));
                }

                await InsertBarcodesAsync(connection, transaction, product, token);
            }, cancellationToken);
        }
        catch (CatalogRuleException)
        {
            throw;
        }
        catch (Exception exception) when (PostgresUnique.AsConflict(exception, "Barcode is already assigned to a product.") is { } conflict)
        {
            throw conflict;
        }
    }

    private static async Task InsertBarcodesAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        Product product,
        CancellationToken cancellationToken)
    {
        var active = product.Barcodes.Where(barcode => !barcode.IsDeleted).ToArray();
        if (active.Length == 0)
        {
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into product_barcodes (id, product_id, barcode, created_at, updated_at, is_deleted)
            values (@Id, @ProductId, @Barcode, @CreatedAt, @UpdatedAt, @IsDeleted)
            on conflict (id) do nothing;
            """,
            active,
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<string>>> LoadBarcodesAsync(
        Npgsql.NpgsqlConnection connection,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<BarcodeLink>(new CommandDefinition(
            """
            select product_id, barcode
            from product_barcodes
            where is_deleted = false and product_id = any(@Ids)
            order by barcode;
            """,
            new { Ids = ids },
            cancellationToken: cancellationToken));
        return rows
            .GroupBy(row => row.ProductId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(row => row.Barcode).ToArray());
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

    private static ProductResponse ToResponse(ProductReadRow row, IReadOnlyList<string> barcodes)
    {
        return new ProductResponse(
            row.Id,
            row.Name,
            row.CategoryId,
            row.CategoryName,
            row.BrandId,
            row.BrandName,
            row.UnitId,
            row.UnitName,
            row.PurchasePrice,
            row.SellingPrice,
            row.MinimumStock,
            row.IsActive,
            barcodes,
            row.CreatedAt,
            row.UpdatedAt);
    }

    private sealed class ProductReadRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public Guid? CategoryId { get; init; }

        public string? CategoryName { get; init; }

        public Guid? BrandId { get; init; }

        public string? BrandName { get; init; }

        public Guid UnitId { get; init; }

        public string UnitName { get; init; } = string.Empty;

        public decimal PurchasePrice { get; init; }

        public decimal SellingPrice { get; init; }

        public decimal MinimumStock { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class ProductWriteRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public Guid? CategoryId { get; init; }

        public Guid? BrandId { get; init; }

        public Guid UnitId { get; init; }

        public decimal PurchasePrice { get; init; }

        public decimal SellingPrice { get; init; }

        public decimal MinimumStock { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class BarcodeWriteRow
    {
        public Guid Id { get; init; }

        public Guid ProductId { get; init; }

        public string Barcode { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class BarcodeLink
    {
        public Guid ProductId { get; init; }

        public string Barcode { get; init; } = string.Empty;
    }
}
