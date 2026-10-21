using System.Text.Json;
using Dapper;
using Npgsql;
using Retail360.Application.Catalog;
using Retail360.Infrastructure.Catalog;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("catalog-database", DisableParallelization = true)]
public sealed class CatalogDatabaseCollection : ICollectionFixture<CatalogDatabaseFixture>
{
}

[Collection("catalog-database")]
public sealed class CatalogDatabaseTests
{
    private readonly CatalogDatabaseFixture _database;

    public CatalogDatabaseTests(CatalogDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_other_databases_without_connecting()
    {
        Assert.Equal("retail360_catalog_test", CatalogDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => CatalogDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => CatalogDatabaseGuard.Require("retail360_inventory_test"));
        CatalogDatabaseGuard.Require(CatalogDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Runner_records_each_script_once_and_keeps_identity_roles()
    {
        await _database.Runner.ApplyAsync(CancellationToken.None);

        await using var connection = await _database.OpenAsync();
        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations order by version;")).ToArray();
        Assert.Equal(
            ["001_initial_identity", "002_catalog", "003_inventory", "004_stock_operations", "005_numeric_scale", "006_suppliers", "007_purchases", "008_sales"],
            versions);

        var roles = new ShopRoleQuery(_database.Connections);
        await roles.EnsureRequiredRolesAsync(CancellationToken.None);
        var roleIds = (await connection.QueryAsync<string>(
            """
            select id
            from asp_net_roles
            where is_deleted = false
            order by id;
            """)).ToArray();
        Assert.Equal(
            [
                "8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e01",
                "8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e02",
                "8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e03",
                "8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e04"
            ],
            roleIds);
    }

    [Fact]
    public async Task Product_table_matches_the_approved_shape()
    {
        await using var connection = await _database.OpenAsync();
        var columns = (await connection.QueryAsync<ColumnRow>(
            """
            select column_name, data_type, numeric_precision, numeric_scale, is_nullable
            from information_schema.columns
            where table_schema = 'public' and table_name = 'products';
            """)).ToDictionary(column => column.ColumnName, StringComparer.Ordinal);

        Assert.Equal("numeric", columns["purchase_price"].DataType);
        Assert.Null(columns["purchase_price"].NumericPrecision);
        Assert.Null(columns["purchase_price"].NumericScale);
        Assert.Equal("numeric", columns["selling_price"].DataType);
        Assert.Null(columns["selling_price"].NumericPrecision);
        Assert.Null(columns["selling_price"].NumericScale);
        Assert.Equal("numeric", columns["minimum_stock"].DataType);
        Assert.Null(columns["minimum_stock"].NumericPrecision);
        Assert.Null(columns["minimum_stock"].NumericScale);
        var scaleChecks = (await connection.QueryAsync<string>(
            """
            select conname
            from pg_constraint
            where conname in (
                'ck_products_purchase_price_scale',
                'ck_products_selling_price_scale',
                'ck_products_minimum_stock_scale')
            order by conname;
            """)).ToArray();
        Assert.Equal(
            [
                "ck_products_minimum_stock_scale",
                "ck_products_purchase_price_scale",
                "ck_products_selling_price_scale"
            ],
            scaleChecks);
        Assert.Equal("YES", columns["category_id"].IsNullable);
        Assert.Equal("YES", columns["brand_id"].IsNullable);
        Assert.Equal("NO", columns["unit_id"].IsNullable);
        Assert.DoesNotContain("sku", columns.Keys);
        Assert.DoesNotContain("stock", columns.Keys);
        Assert.DoesNotContain("current_stock", columns.Keys);
        Assert.DoesNotContain("available_quantity", columns.Keys);

        var indexes = (await connection.QueryAsync<string>(
            """
            select indexdef
            from pg_indexes
            where schemaname = 'public'
              and indexname in (
                'uq_categories_name',
                'uq_brands_name',
                'uq_units_name',
                'uq_product_barcodes_barcode');
            """)).ToArray();
        Assert.Equal(4, indexes.Length);
        Assert.All(indexes, index =>
        {
            Assert.Contains("UNIQUE", index, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("is_deleted = false", index, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Database_rejects_negative_price_null_unit_and_duplicate_barcode()
    {
        await using (var connection = await _database.OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var unitId = Guid.NewGuid();
            await InsertUnitAsync(connection, transaction, unitId, $"Piece {unitId:N}");
            var priceError = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                """
                insert into products (
                    id, name, category_id, brand_id, unit_id,
                    purchase_price, selling_price, minimum_stock, is_active,
                    created_at, updated_at, is_deleted)
                values (
                    @Id, 'Rice', null, null, @UnitId,
                    -1, 2, 0, true,
                    now(), now(), false);
                """,
                new { Id = Guid.NewGuid(), UnitId = unitId },
                transaction));
            Assert.Equal(PostgresErrorCodes.CheckViolation, priceError.SqlState);
            await transaction.RollbackAsync();
        }

        await using (var connection = await _database.OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var unitId = Guid.NewGuid();
            await InsertUnitAsync(connection, transaction, unitId, $"Piece {unitId:N}");
            var extraPrice = await RejectProductAsync(connection, transaction, unitId, 1.235m, 0m);
            var extraStock = await RejectProductAsync(connection, transaction, unitId, 1.00m, 1.2345m);
            var abovePrice = await RejectProductAsync(connection, transaction, unitId, 10000000000.00m, 0m);
            var productId = Guid.NewGuid();
            await connection.ExecuteAsync(
                """
                insert into products (
                    id, name, category_id, brand_id, unit_id,
                    purchase_price, selling_price, minimum_stock, is_active,
                    created_at, updated_at, is_deleted)
                values (
                    @Id, 'Rice', null, null, @UnitId,
                    10.10, 10.10, 1.2300, true,
                    now(), now(), false);
                """,
                new { Id = productId, UnitId = unitId },
                transaction);
            var stored = await connection.QuerySingleAsync<(decimal PurchasePrice, decimal MinimumStock)>(
                """
                select purchase_price, minimum_stock
                from products
                where id = @Id;
                """,
                new { Id = productId },
                transaction);

            Assert.Equal(PostgresErrorCodes.CheckViolation, extraPrice.SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, extraStock.SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, abovePrice.SqlState);
            Assert.Equal(10.10m, stored.PurchasePrice);
            Assert.Equal(1.230m, stored.MinimumStock);
            await transaction.RollbackAsync();
        }

        await using (var connection = await _database.OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                """
                insert into products (
                    id, name, category_id, brand_id, unit_id,
                    purchase_price, selling_price, minimum_stock, is_active,
                    created_at, updated_at, is_deleted)
                values (
                    @Id, 'Rice', null, null, null,
                    1, 2, 0, true,
                    now(), now(), false);
                """,
                new { Id = Guid.NewGuid() },
                transaction));
            Assert.Equal(PostgresErrorCodes.NotNullViolation, error.SqlState);
            await transaction.RollbackAsync();
        }

        await using (var connection = await _database.OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var unitId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            const string barcode = "duplicate-barcode";
            await InsertUnitAsync(connection, transaction, unitId, $"Piece {unitId:N}");
            await InsertProductAsync(connection, transaction, productId, unitId, "Rice");
            var otherProductId = Guid.NewGuid();
            await InsertProductAsync(connection, transaction, otherProductId, unitId, "Other rice");
            await InsertBarcodeAsync(connection, transaction, productId, barcode);
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertBarcodeAsync(connection, transaction, otherProductId, barcode));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Soft_deleted_barcode_and_category_name_can_be_reused()
    {
        await using (var connection = await _database.OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var unitId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            const string barcode = "reusable-barcode";
            await InsertUnitAsync(connection, transaction, unitId, $"Piece {unitId:N}");
            await InsertProductAsync(connection, transaction, productId, unitId, "Rice");
            await InsertBarcodeAsync(connection, transaction, productId, barcode);
            await connection.ExecuteAsync(
                """
                update product_barcodes
                set is_deleted = true, updated_at = now()
                where barcode = @Barcode;
                """,
                new { Barcode = barcode },
                transaction);
            await InsertBarcodeAsync(connection, transaction, productId, barcode);
            await transaction.RollbackAsync();
        }

        await using (var connection = await _database.OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var firstId = Guid.NewGuid();
            var name = $"Grocery {firstId:N}";
            await InsertCategoryAsync(connection, transaction, firstId, name);
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertCategoryAsync(connection, transaction, Guid.NewGuid(), name.ToUpperInvariant()));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Stored_product_lookup_hides_inactive_barcodes_and_deleted_products()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var barcode = $"scan-{suffix}";
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        var categories = new CategoryCatalogStore(_database.Connections);
        var brands = new BrandCatalogStore(_database.Connections);
        var units = new UnitCatalogStore(_database.Connections);
        var products = new ProductService(
            new ProductCatalogStore(_database.Connections, new PostgresTransaction(_database.Connections)),
            categories,
            brands,
            units,
            clock);
        var unitService = new UnitService(units, clock);
        var unit = await unitService.CreateAsync(new SaveNameCommand($"Kg {suffix}"), CancellationToken.None);

        var created = await products.CreateAsync(
            new SaveProductCommand($"Rice {suffix}", null, null, unit.Id, 80.50m, 100.00m, 1.500m, [barcode]),
            CancellationToken.None);
        var byBarcode = await products.FindActiveByBarcodeAsync(barcode, CancellationToken.None);
        Assert.Equal(created.Id, byBarcode.Id);
        Assert.Null(created.CategoryId);
        Assert.Null(created.BrandId);

        await products.DeactivateAsync(created.Id, CancellationToken.None);
        var inactive = await products.GetAsync(created.Id, CancellationToken.None);
        var hiddenBarcode = await Assert.ThrowsAsync<Retail360.Domain.Catalog.CatalogRuleException>(() =>
            products.FindActiveByBarcodeAsync(barcode, CancellationToken.None));
        Assert.False(inactive.IsActive);
        Assert.Equal(Retail360.Domain.Catalog.CatalogRuleFailure.NotFound, hiddenBarcode.Failure);

        await products.DeleteAsync(created.Id, CancellationToken.None);
        var deleted = await Assert.ThrowsAsync<Retail360.Domain.Catalog.CatalogRuleException>(() =>
            products.GetAsync(created.Id, CancellationToken.None));
        var listed = await products.ListAsync(suffix, false, 50, 0, CancellationToken.None);
        Assert.Equal(Retail360.Domain.Catalog.CatalogRuleFailure.NotFound, deleted.Failure);
        Assert.DoesNotContain(listed, product => product.Id == created.Id);

        var reused = await products.CreateAsync(
            new SaveProductCommand($"Rice again {suffix}", null, null, unit.Id, 80.50m, 100.00m, 1.500m, [barcode]),
            CancellationToken.None);
        Assert.Equal(barcode, Assert.Single(reused.Barcodes));
    }

    [Fact]
    public async Task Stored_product_keeps_several_barcodes_including_leading_zeros()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        var products = new ProductService(
            new ProductCatalogStore(_database.Connections, new PostgresTransaction(_database.Connections)),
            new CategoryCatalogStore(_database.Connections),
            new BrandCatalogStore(_database.Connections),
            new UnitCatalogStore(_database.Connections),
            clock);
        var unit = await new UnitService(new UnitCatalogStore(_database.Connections), clock)
            .CreateAsync(new SaveNameCommand($"Pack {suffix}"), CancellationToken.None);

        var leadingZero = $"00{suffix}";
        var created = await products.CreateAsync(
            new SaveProductCommand($"Flour {suffix}", null, null, unit.Id, 1.00m, 2.00m, 0m, [leadingZero, $"SHOP-{suffix}"]),
            CancellationToken.None);
        var loaded = await products.GetAsync(created.Id, CancellationToken.None);

        Assert.Equal([leadingZero, $"SHOP-{suffix}"], loaded.Barcodes);
        var byBarcode = await products.FindActiveByBarcodeAsync(leadingZero, CancellationToken.None);
        Assert.Equal(created.Id, byBarcode.Id);
    }

    private static Task InsertUnitAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        string name)
    {
        return connection.ExecuteAsync(
            """
            insert into units (id, name, created_at, updated_at, is_deleted)
            values (@Id, @Name, now(), now(), false);
            """,
            new { Id = id, Name = name },
            transaction);
    }

    private static Task InsertCategoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        string name)
    {
        return connection.ExecuteAsync(
            """
            insert into categories (id, name, created_at, updated_at, is_deleted)
            values (@Id, @Name, now(), now(), false);
            """,
            new { Id = id, Name = name },
            transaction);
    }

    private static async Task<PostgresException> RejectProductAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid unitId,
        decimal purchasePrice,
        decimal minimumStock)
    {
        await transaction.SaveAsync("product_scale");
        try
        {
            await connection.ExecuteAsync(
                """
                insert into products (
                    id, name, category_id, brand_id, unit_id,
                    purchase_price, selling_price, minimum_stock, is_active,
                    created_at, updated_at, is_deleted)
                values (
                    @Id, 'Rice', null, null, @UnitId,
                    @PurchasePrice, 2.00, @MinimumStock, true,
                    now(), now(), false);
                """,
                new { Id = Guid.NewGuid(), UnitId = unitId, PurchasePrice = purchasePrice, MinimumStock = minimumStock },
                transaction);
            throw new InvalidOperationException("The product insert was expected to fail.");
        }
        catch (PostgresException exception)
        {
            await transaction.RollbackAsync("product_scale");
            return exception;
        }
    }

    private static Task InsertProductAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        Guid unitId,
        string name)
    {
        return connection.ExecuteAsync(
            """
            insert into products (
                id, name, category_id, brand_id, unit_id,
                purchase_price, selling_price, minimum_stock, is_active,
                created_at, updated_at, is_deleted)
            values (
                @Id, @Name, null, null, @UnitId,
                1.00, 2.00, 0.000, true,
                now(), now(), false);
            """,
            new { Id = id, Name = name, UnitId = unitId },
            transaction);
    }

    private static Task InsertBarcodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid productId,
        string barcode)
    {
        return connection.ExecuteAsync(
            """
            insert into product_barcodes (id, product_id, barcode, created_at, updated_at, is_deleted)
            values (@Id, @ProductId, @Barcode, now(), now(), false);
            """,
            new { Id = Guid.NewGuid(), ProductId = productId, Barcode = barcode },
            transaction);
    }

    private sealed class ColumnRow
    {
        public string ColumnName { get; init; } = string.Empty;

        public string DataType { get; init; } = string.Empty;

        public int? NumericPrecision { get; init; }

        public int? NumericScale { get; init; }

        public string IsNullable { get; init; } = string.Empty;
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}

public sealed class CatalogDatabaseFixture : IAsyncLifetime
{
    public NpgsqlConnectionFactory Connections { get; private set; } = null!;

    public SqlMigrationRunner Runner { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var shop = TestShopConnection.Read();
        var builder = new NpgsqlConnectionStringBuilder(shop)
        {
            Database = "postgres"
        };
        await EnsureDatabaseAsync(builder.ConnectionString);
        builder.Database = CatalogDatabaseGuard.PermittedDatabase;
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            CatalogDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        Runner = new SqlMigrationRunner(Connections);
        await Runner.ApplyAsync(CancellationToken.None);
        await Runner.ApplyAsync(CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        CatalogDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
    }

    private static async Task EnsureDatabaseAsync(string adminConnectionString)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "select exists(select 1 from pg_database where datname = @Name);",
            new { Name = CatalogDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {CatalogDatabaseGuard.PermittedDatabase};");
    }
}
