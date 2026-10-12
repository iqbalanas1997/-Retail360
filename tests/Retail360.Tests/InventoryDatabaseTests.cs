using System.Text.Json;
using Dapper;
using Npgsql;
using Retail360.Application.Catalog;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Inventory;
using Retail360.Infrastructure.Persistence.Sql;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("inventory-database", DisableParallelization = true)]
public sealed class InventoryDatabaseCollection : ICollectionFixture<InventoryDatabaseFixture>
{
}

[Collection("inventory-database")]
public sealed class InventoryDatabaseTests
{
    private readonly InventoryDatabaseFixture _database;

    public InventoryDatabaseTests(InventoryDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_the_shop_database_without_connecting()
    {
        Assert.Equal("retail360_inventory_test", InventoryDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => InventoryDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => InventoryDatabaseGuard.Require("retail360_catalog_test"));
        InventoryDatabaseGuard.Require(InventoryDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Runner_applies_inventory_once_on_the_isolated_database()
    {
        await _database.Runner.ApplyAsync(CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        InventoryDatabaseGuard.Require(name);

        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations order by version;")).ToArray();
        Assert.Equal(["001_initial_identity", "002_catalog", "003_inventory", "004_stock_operations"], versions);

        var tables = (await connection.QueryAsync<string>(
            """
            select table_name
            from information_schema.tables
            where table_schema = 'public'
              and table_name in ('areas', 'shelves', 'stock_balances', 'stock_movements', 'stock_operations')
            order by table_name;
            """)).ToArray();
        Assert.Equal(["areas", "shelves", "stock_balances", "stock_movements", "stock_operations"], tables);
        var otherShelfIndex = await connection.ExecuteScalarAsync<string>(
            """
            select indexname
            from pg_indexes
            where schemaname = 'public' and indexname = 'ix_stock_movements_other_shelf_id';
            """);
        Assert.Equal("ix_stock_movements_other_shelf_id", otherShelfIndex);
    }

    [Fact]
    public async Task Areas_and_shelves_follow_the_lifecycle_rules()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var area = await graph.Areas.CreateAsync(new SaveAreaCommand(" Front "), CancellationToken.None);
        var shelf = await graph.Shelves.CreateAsync(new SaveShelfCommand(area.Id, " A1 "), CancellationToken.None);
        var loaded = await graph.Shelves.GetAsync(shelf.Id, CancellationToken.None);

        Assert.Equal("Front", area.Name);
        Assert.Equal("A1", loaded.Name);
        Assert.Equal(area.Id, loaded.AreaId);

        var duplicate = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Areas.CreateAsync(new SaveAreaCommand("front"), CancellationToken.None));
        Assert.Equal(InventoryRuleFailure.Conflict, duplicate.Failure);

        await graph.Shelves.DeleteAsync(shelf.Id, CancellationToken.None);
        await graph.Areas.DeleteAsync(area.Id, CancellationToken.None);
        var missing = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Areas.GetAsync(area.Id, CancellationToken.None));
        Assert.Equal(InventoryRuleFailure.NotFound, missing.Failure);
    }

    [Fact]
    public async Task Soft_delete_is_rejected_after_stock_history_exists()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        await graph.Stock.OpenAsync(
            new OpeningStockCommand(setup.ProductId, setup.ShelfId, 1.000m, "opening", Guid.NewGuid()),
            CancellationToken.None);

        var shelfError = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Shelves.DeleteAsync(setup.ShelfId, CancellationToken.None));
        var areaError = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Areas.DeleteAsync(setup.AreaId, CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, shelfError.Failure);
        Assert.Equal(InventoryRuleFailure.Conflict, areaError.Failure);
        Assert.Equal("A1", (await graph.Shelves.GetAsync(setup.ShelfId, CancellationToken.None)).Name);
    }

    [Fact]
    public async Task Opening_stock_matches_the_balance_and_rejects_bad_input()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 2.000m, "Front", "A1");

        var movements = await graph.Stock.OpenAsync(
            new OpeningStockCommand(setup.ProductId, setup.ShelfId, 2.500m, "opening", Guid.NewGuid()),
            CancellationToken.None);
        var balance = Assert.Single(await graph.Stock.ListBalancesAsync(setup.ProductId, setup.ShelfId, CancellationToken.None));
        Assert.Equal(2.500m, balance.Quantity);
        Assert.Equal("opening", Assert.Single(movements).MovementType);

        await graph.Shelves.DeactivateAsync(setup.ShelfId, CancellationToken.None);
        var inactive = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.OpenAsync(
                new OpeningStockCommand(setup.ProductId, setup.ShelfId, 1m, "more", Guid.NewGuid()),
                CancellationToken.None));
        var invalid = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.OpenAsync(
                new OpeningStockCommand(setup.ProductId, setup.ShelfId, 0m, "zero", Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, inactive.Failure);
        Assert.Equal(InventoryRuleFailure.Invalid, invalid.Failure);
        Assert.Equal(2.500m, (await graph.Stock.ListBalancesAsync(setup.ProductId, null, CancellationToken.None)).Single().Quantity);
        Assert.Single(await graph.Stock.ListMovementsAsync(setup.ProductId, null, 50, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Adjustments_cover_reasons_inactive_products_and_negative_stock()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        await graph.Stock.OpenAsync(new OpeningStockCommand(setup.ProductId, setup.ShelfId, 5.000m, "opening", Guid.NewGuid()), CancellationToken.None);
        await graph.SetProductActiveAsync(setup.ProductId, false);

        await graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, 1.000m, "count", Guid.NewGuid()), CancellationToken.None);
        await graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, -1.000m, InventoryText.Damaged, Guid.NewGuid()), CancellationToken.None);
        await graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, -1.000m, InventoryText.Expired, Guid.NewGuid()), CancellationToken.None);
        await graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, -1.000m, InventoryText.Missing, Guid.NewGuid()), CancellationToken.None);
        await graph.Shelves.DeactivateAsync(setup.ShelfId, CancellationToken.None);
        await graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, -1.000m, "correction", Guid.NewGuid()), CancellationToken.None);

        var inbound = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, 1.000m, "count", Guid.NewGuid()), CancellationToken.None));
        var shortfall = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, -3.000m, "too much", Guid.NewGuid()), CancellationToken.None));
        await graph.SetProductDeletedAsync(setup.ProductId);
        var deleted = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, -1.000m, "gone", Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, inbound.Failure);
        Assert.Equal(InventoryRuleFailure.Conflict, shortfall.Failure);
        Assert.Equal(InventoryRuleFailure.NotFound, deleted.Failure);
        Assert.Equal(2.000m, (await BalanceAsync(setup.ProductId, setup.ShelfId)));

        await using var connection = await _database.OpenAsync();
        var movementId = await connection.ExecuteScalarAsync<Guid>("select id from stock_movements limit 1;");
        var update = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "update stock_movements set reason = 'changed' where id = @Id;",
            new { Id = movementId }));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "delete from stock_movements where id = @Id;",
            new { Id = movementId }));
        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
    }

    [Fact]
    public async Task Transfer_updates_both_shelves_or_neither()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var source = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        var destination = await graph.Shelves.CreateAsync(new SaveShelfCommand(source.AreaId, "B1"), CancellationToken.None);
        var otherArea = await graph.Areas.CreateAsync(new SaveAreaCommand("Back"), CancellationToken.None);
        var inactive = await graph.Shelves.CreateAsync(new SaveShelfCommand(otherArea.Id, "C1"), CancellationToken.None);
        await graph.Stock.OpenAsync(new OpeningStockCommand(source.ProductId, source.ShelfId, 5.000m, "opening", Guid.NewGuid()), CancellationToken.None);
        await graph.Shelves.DeactivateAsync(source.ShelfId, CancellationToken.None);

        var moved = await graph.Stock.TransferAsync(
            new TransferCommand(source.ProductId, source.ShelfId, destination.Id, 2.000m, null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(2, moved.Count);
        Assert.Equal(moved[0].OperationId, moved[1].OperationId);
        Assert.Equal(3.000m, await BalanceAsync(source.ProductId, source.ShelfId));
        Assert.Equal(2.000m, await BalanceAsync(source.ProductId, destination.Id));

        await graph.Shelves.DeactivateAsync(inactive.Id, CancellationToken.None);
        var blocked = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.TransferAsync(
                new TransferCommand(source.ProductId, source.ShelfId, inactive.Id, 1.000m, "move", Guid.NewGuid()),
                CancellationToken.None));
        var same = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.TransferAsync(
                new TransferCommand(source.ProductId, source.ShelfId, source.ShelfId, 1.000m, "move", Guid.NewGuid()),
                CancellationToken.None));
        var shortfall = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            graph.Stock.TransferAsync(
                new TransferCommand(source.ProductId, source.ShelfId, destination.Id, 4.000m, "move", Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, blocked.Failure);
        Assert.Equal(InventoryRuleFailure.Invalid, same.Failure);
        Assert.Equal(InventoryRuleFailure.Conflict, shortfall.Failure);
        Assert.Equal(3.000m, await BalanceAsync(source.ProductId, source.ShelfId));
        Assert.Equal(2.000m, await BalanceAsync(source.ProductId, destination.Id));
        Assert.Empty(await graph.Stock.ListBalancesAsync(source.ProductId, inactive.Id, CancellationToken.None));
        Assert.Equal(3, (await graph.Stock.ListMovementsAsync(source.ProductId, null, 50, 0, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Two_waiting_outbound_adjustments_cannot_overspend_after_the_product_lock_is_released()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        await graph.Stock.OpenAsync(new OpeningStockCommand(setup.ProductId, setup.ShelfId, 5.000m, "opening", Guid.NewGuid()), CancellationToken.None);

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from products where id = @Id for update;",
            new { Id = setup.ProductId },
            gate);
        var first = RunOnOwnThread(() => graph.Stock.AdjustAsync(
            new AdjustmentCommand(setup.ProductId, setup.ShelfId, -4.000m, "one", Guid.NewGuid()),
            CancellationToken.None));
        var second = RunOnOwnThread(() => graph.Stock.AdjustAsync(
            new AdjustmentCommand(setup.ProductId, setup.ShelfId, -4.000m, "two", Guid.NewGuid()),
            CancellationToken.None));
        await WaitForWaitingSessionsAsync(holder, gate, 2);
        await gate.CommitAsync();

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, results.Count(result => result?.Failure == InventoryRuleFailure.Conflict));
        Assert.Equal(1.000m, await BalanceAsync(setup.ProductId, setup.ShelfId));
        var movements = await graph.Stock.ListMovementsAsync(setup.ProductId, setup.ShelfId, 50, 0, CancellationToken.None);
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, movement => movement.MovementType == "adjustment" && movement.Direction == "out" && movement.Quantity == 4.000m);
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Shelf_delete_rechecks_history_after_waiting_on_the_shelf_lock()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from shelves where id = @Id for update;",
            new { Id = setup.ShelfId },
            gate);

        var delete = graph.Shelves.DeleteAsync(setup.ShelfId, CancellationToken.None);
        await WaitForWaitingSessionsAsync(holder, gate, 1);

        await holder.ExecuteAsync(
            """
            insert into stock_balances (id, product_id, shelf_id, quantity, created_at, updated_at)
            values (@BalanceId, @ProductId, @ShelfId, 1.000, now(), now());
            insert into stock_movements (
                id, operation_id, product_id, shelf_id, quantity, direction, movement_type,
                reason, other_shelf_id, occurred_at, created_at)
            values (
                @MovementId, @OperationId, @ProductId, @ShelfId, 1.000, 'in', 'opening',
                'opening', null, now(), now());
            """,
            new
            {
                BalanceId = Guid.NewGuid(),
                MovementId = Guid.NewGuid(),
                OperationId = Guid.NewGuid(),
                setup.ProductId,
                setup.ShelfId
            },
            gate);
        await gate.CommitAsync();

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() => delete);
        Assert.Equal(InventoryRuleFailure.Conflict, error.Failure);
        Assert.Equal("A1", (await graph.Shelves.GetAsync(setup.ShelfId, CancellationToken.None)).Name);
        Assert.Equal(1.000m, await BalanceAsync(setup.ProductId, setup.ShelfId));
        Assert.Single(await graph.Stock.ListMovementsAsync(setup.ProductId, setup.ShelfId, 50, 0, CancellationToken.None));
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stock_post_rechecks_product_deletion_after_waiting_on_the_product_lock()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from products where id = @Id for update;",
            new { Id = setup.ProductId },
            gate);

        var opening = graph.Stock.OpenAsync(
            new OpeningStockCommand(setup.ProductId, setup.ShelfId, 1.000m, "opening", Guid.NewGuid()),
            CancellationToken.None);
        await WaitForWaitingSessionsAsync(holder, gate, 1);
        await holder.ExecuteAsync(
            "update products set is_deleted = true, updated_at = now() where id = @Id;",
            new { Id = setup.ProductId },
            gate);
        await gate.CommitAsync();

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() => opening);
        Assert.Equal(InventoryRuleFailure.NotFound, error.Failure);
        Assert.Empty(await graph.Stock.ListMovementsAsync(setup.ProductId, null, 50, 0, CancellationToken.None));
        Assert.Empty(await graph.Stock.ListBalancesAsync(setup.ProductId, null, CancellationToken.None));
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));

        await using var connection = await _database.OpenAsync();
        var deleted = await connection.ExecuteScalarAsync<bool>(
            "select is_deleted from products where id = @Id;",
            new { Id = setup.ProductId });
        Assert.True(deleted);
    }

    [Fact]
    public async Task Two_waiting_transfers_from_the_same_source_cannot_overspend()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var source = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        var destination = await graph.Shelves.CreateAsync(new SaveShelfCommand(source.AreaId, "B1"), CancellationToken.None);
        var other = await graph.Shelves.CreateAsync(new SaveShelfCommand(source.AreaId, "C1"), CancellationToken.None);
        await graph.Stock.OpenAsync(new OpeningStockCommand(source.ProductId, source.ShelfId, 5.000m, "opening", Guid.NewGuid()), CancellationToken.None);

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from products where id = @Id for update;",
            new { Id = source.ProductId },
            gate);
        var first = RunOnOwnThread(() => graph.Stock.TransferAsync(
            new TransferCommand(source.ProductId, source.ShelfId, destination.Id, 4.000m, "one", Guid.NewGuid()),
            CancellationToken.None));
        var second = RunOnOwnThread(() => graph.Stock.TransferAsync(
            new TransferCommand(source.ProductId, source.ShelfId, other.Id, 4.000m, "two", Guid.NewGuid()),
            CancellationToken.None));
        await WaitForWaitingSessionsAsync(holder, gate, 2);
        await gate.CommitAsync();

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, results.Count(result => result?.Failure == InventoryRuleFailure.Conflict));
        Assert.Equal(1.000m, await BalanceAsync(source.ProductId, source.ShelfId));
        var destinationBalance = await BalanceOrZeroAsync(source.ProductId, destination.Id);
        var otherBalance = await BalanceOrZeroAsync(source.ProductId, other.Id);
        Assert.Equal(4.000m, destinationBalance + otherBalance);
        Assert.Equal(5.000m, 1.000m + destinationBalance + otherBalance);
        var movements = await graph.Stock.ListMovementsAsync(source.ProductId, null, 50, 0, CancellationToken.None);
        Assert.Equal(3, movements.Count);
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Area_delete_rechecks_shelves_after_waiting_on_the_area_lock()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var area = await graph.Areas.CreateAsync(new SaveAreaCommand("Front"), CancellationToken.None);

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from areas where id = @Id for update;",
            new { Id = area.Id },
            gate);

        var delete = graph.Areas.DeleteAsync(area.Id, CancellationToken.None);
        await WaitForWaitingSessionsAsync(holder, gate, 1);
        await holder.ExecuteAsync(
            """
            insert into shelves (id, area_id, name, is_active, created_at, updated_at, is_deleted)
            values (@Id, @AreaId, 'A1', true, now(), now(), false);
            """,
            new { Id = Guid.NewGuid(), AreaId = area.Id },
            gate);
        await gate.CommitAsync();

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() => delete);
        Assert.Equal(InventoryRuleFailure.Conflict, error.Failure);
        Assert.Equal("Front", (await graph.Areas.GetAsync(area.Id, CancellationToken.None)).Name);
        Assert.Single(await graph.Shelves.ListAsync(area.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Balance_queries_keep_stock_when_an_area_row_is_soft_deleted()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        await graph.Stock.OpenAsync(new OpeningStockCommand(setup.ProductId, setup.ShelfId, 2.000m, "opening", Guid.NewGuid()), CancellationToken.None);

        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            "update areas set is_deleted = true, updated_at = now() where id = @Id;",
            new { Id = setup.AreaId });

        var balance = Assert.Single(await graph.Stock.ListBalancesAsync(setup.ProductId, setup.ShelfId, CancellationToken.None));
        Assert.Equal(2.000m, balance.Quantity);
        Assert.Equal("Front", balance.AreaName);
        Assert.Single(await graph.Stock.ListMovementsAsync(setup.ProductId, setup.ShelfId, 50, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Repeating_a_completed_opening_does_not_post_again()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        var operationId = Guid.NewGuid();
        var command = new OpeningStockCommand(setup.ProductId, setup.ShelfId, 2.000m, "opening", operationId);

        var first = await graph.Stock.OpenAsync(command, CancellationToken.None);
        var second = await graph.Stock.OpenAsync(command, CancellationToken.None);

        Assert.Equal(first.Select(movement => movement.Id), second.Select(movement => movement.Id));
        Assert.Equal(2.000m, await BalanceAsync(setup.ProductId, setup.ShelfId));
        Assert.Single(await graph.Stock.ListMovementsAsync(setup.ProductId, null, 50, 0, CancellationToken.None));
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reusing_an_operation_key_with_a_different_payload_is_a_conflict()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        var operationId = Guid.NewGuid();
        await graph.Stock.OpenAsync(
            new OpeningStockCommand(setup.ProductId, setup.ShelfId, 2.000m, "opening", operationId),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() => graph.Stock.OpenAsync(
            new OpeningStockCommand(setup.ProductId, setup.ShelfId, 3.000m, "opening", operationId),
            CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, error.Failure);
        Assert.Equal(2.000m, await BalanceAsync(setup.ProductId, setup.ShelfId));
        Assert.Single(await graph.Stock.ListMovementsAsync(setup.ProductId, null, 50, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Transfer_retry_returns_both_movements_without_posting_again()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var source = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        var destination = await graph.Shelves.CreateAsync(new SaveShelfCommand(source.AreaId, "B1"), CancellationToken.None);
        await graph.Stock.OpenAsync(new OpeningStockCommand(source.ProductId, source.ShelfId, 5.000m, "opening", Guid.NewGuid()), CancellationToken.None);
        var command = new TransferCommand(source.ProductId, source.ShelfId, destination.Id, 2.000m, "move", Guid.NewGuid());

        var first = await graph.Stock.TransferAsync(command, CancellationToken.None);
        var second = await graph.Stock.TransferAsync(command, CancellationToken.None);

        Assert.Equal(2, first.Count);
        Assert.Equal(first.Select(movement => movement.Id), second.Select(movement => movement.Id));
        Assert.Equal(3.000m, await BalanceAsync(source.ProductId, source.ShelfId));
        Assert.Equal(2.000m, await BalanceAsync(source.ProductId, destination.Id));
        Assert.Equal(3, (await graph.Stock.ListMovementsAsync(source.ProductId, null, 50, 0, CancellationToken.None)).Count);
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_operation_key_post_once()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        var operationId = Guid.NewGuid();
        var command = new OpeningStockCommand(setup.ProductId, setup.ShelfId, 1.000m, "opening", operationId);

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from products where id = @Id for update;",
            new { Id = setup.ProductId },
            gate);
        var first = RunOnOwnThread(() => graph.Stock.OpenAsync(command, CancellationToken.None));
        var second = RunOnOwnThread(() => graph.Stock.OpenAsync(command, CancellationToken.None));
        await WaitForWaitingSessionsAsync(holder, gate, 2);
        await gate.CommitAsync();

        var results = await Task.WhenAll(first, second);
        Assert.Equal(results[0].Select(movement => movement.Id), results[1].Select(movement => movement.Id));
        Assert.Equal(1.000m, await BalanceAsync(setup.ProductId, setup.ShelfId));
        Assert.Single(await graph.Stock.ListMovementsAsync(setup.ProductId, null, 50, 0, CancellationToken.None));
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_check_constraint_other_than_balance_is_not_reported_as_insufficient_stock()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        await graph.Stock.OpenAsync(new OpeningStockCommand(setup.ProductId, setup.ShelfId, 2.000m, "opening", Guid.NewGuid()), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() => graph.Ledger.PostAsync(
            new StockPosting(
                Guid.NewGuid(),
                "adjustment-without-reason",
                setup.ProductId,
                [new StockLine(setup.ShelfId, 1.000m, StockDirection.In, StockMovementType.Adjustment, "", null)],
                DateTimeOffset.UtcNow),
            CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, error.Failure);
        Assert.Equal("The stock change was rejected.", error.Message);
        Assert.Equal(2.000m, await BalanceAsync(setup.ProductId, setup.ShelfId));
        Assert.Single(await graph.Stock.ListMovementsAsync(setup.ProductId, null, 50, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Queries_include_inactive_shelves_and_reconciliation_detects_a_mismatch()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 3.000m, "Front", "A1");
        var other = await graph.SeedProductAndShelfAsync("Soap", 0.000m, "Back", "B1");
        var second = await graph.Shelves.CreateAsync(new SaveShelfCommand(setup.AreaId, "A2"), CancellationToken.None);
        await graph.Stock.OpenAsync(new OpeningStockCommand(setup.ProductId, setup.ShelfId, 2.000m, "opening", Guid.NewGuid()), CancellationToken.None);
        await graph.Stock.OpenAsync(new OpeningStockCommand(setup.ProductId, second.Id, 2.000m, "opening", Guid.NewGuid()), CancellationToken.None);
        await graph.Shelves.DeactivateAsync(second.Id, CancellationToken.None);

        var totals = await graph.Stock.ListProductStockAsync("all", 50, 0, CancellationToken.None);
        var rice = Assert.Single(totals, row => row.ProductId == setup.ProductId);
        var soap = Assert.Single(totals, row => row.ProductId == other.ProductId);
        Assert.Equal(4.000m, rice.OnHand);
        Assert.Equal("ok", rice.Status);
        Assert.Equal(0.000m, soap.OnHand);
        Assert.Equal("out", soap.Status);

        await graph.Stock.AdjustAsync(new AdjustmentCommand(setup.ProductId, setup.ShelfId, -2.000m, "count", Guid.NewGuid()), CancellationToken.None);
        var low = await graph.Stock.ListProductStockAsync("low", 50, 0, CancellationToken.None);
        Assert.Contains(low, row => row.ProductId == setup.ProductId && row.OnHand == 2.000m);

        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        InventoryDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        await connection.ExecuteAsync(
            """
            update stock_balances
            set quantity = quantity + 1
            where product_id = @ProductId and shelf_id = @ShelfId;
            """,
            new { setup.ProductId, setup.ShelfId });
        var mismatch = Assert.Single(await graph.Stock.ReconcileAsync(CancellationToken.None));
        Assert.Equal(setup.ProductId, mismatch.ProductId);
        Assert.NotEqual(mismatch.StoredQuantity, mismatch.LedgerQuantity);
        await connection.ExecuteAsync(
            """
            update stock_balances
            set quantity = quantity - 1
            where product_id = @ProductId and shelf_id = @ShelfId;
            """,
            new { setup.ProductId, setup.ShelfId });
        Assert.Empty(await graph.Stock.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Database_rejects_a_negative_balance_and_extra_quantity_precision()
    {
        await _database.ResetAsync();
        var graph = _database.CreateGraph();
        var setup = await graph.SeedProductAndShelfAsync("Rice", 1.000m, "Front", "A1");
        await using var connection = await _database.OpenAsync();
        var negative = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into stock_balances (id, product_id, shelf_id, quantity, created_at, updated_at)
            values (@Id, @ProductId, @ShelfId, -0.001, now(), now());
            """,
            new { Id = Guid.NewGuid(), setup.ProductId, setup.ShelfId }));
        var zero = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into stock_movements (
                id, operation_id, product_id, shelf_id, quantity, direction, movement_type,
                reason, other_shelf_id, occurred_at, created_at)
            values (
                @Id, @OperationId, @ProductId, @ShelfId, 0, 'in', 'opening',
                'opening', null, now(), now());
            """,
            new { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), setup.ProductId, setup.ShelfId }));
        var overflow = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into stock_movements (
                id, operation_id, product_id, shelf_id, quantity, direction, movement_type,
                reason, other_shelf_id, occurred_at, created_at)
            values (
                @Id, @OperationId, @ProductId, @ShelfId, 100000000000.000, 'in', 'opening',
                'opening', null, now(), now());
            """,
            new { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), setup.ProductId, setup.ShelfId }));

        Assert.Equal(PostgresErrorCodes.CheckViolation, negative.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, zero.SqlState);
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, overflow.SqlState);
    }

    private async Task<decimal> BalanceAsync(Guid productId, Guid shelfId)
    {
        await using var connection = await _database.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal>(
            """
            select quantity
            from stock_balances
            where product_id = @ProductId and shelf_id = @ShelfId;
            """,
            new { ProductId = productId, ShelfId = shelfId });
    }

    private async Task<decimal> BalanceOrZeroAsync(Guid productId, Guid shelfId)
    {
        await using var connection = await _database.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal>(
            """
            select coalesce((
                select quantity
                from stock_balances
                where product_id = @ProductId and shelf_id = @ShelfId
            ), 0);
            """,
            new { ProductId = productId, ShelfId = shelfId });
    }

    private static async Task WaitForWaitingSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int sessionCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long waiting = 0;
        while (DateTime.UtcNow < deadline)
        {
            waiting = await connection.ExecuteScalarAsync<long>(
                """
                select count(distinct l.pid)
                from pg_locks l
                join pg_stat_activity a on a.pid = l.pid
                where l.granted = false
                  and a.datname = current_database();
                """,
                transaction: transaction);
            if (waiting >= sessionCount)
            {
                return;
            }

            await Task.Delay(20);
        }

        var activity = (await connection.QueryAsync<LockWaitRow>(
            """
            select a.pid, a.state, a.wait_event_type, a.wait_event, left(coalesce(a.query, ''), 160) as query
            from pg_stat_activity a
            where a.datname = current_database()
              and a.pid <> pg_backend_pid();
            """,
            transaction: transaction)).ToArray();
        var detail = string.Join(
            " | ",
            activity.Select(row => $"{row.Pid} {row.State} {row.WaitEventType}/{row.WaitEvent} {row.Query}"));
        throw new TimeoutException(
            $"Expected {sessionCount} database session(s) to be waiting on a lock. Last count was {waiting}. {detail}");
    }

    private sealed class LockWaitRow
    {
        public int Pid { get; init; }

        public string State { get; init; } = string.Empty;

        public string? WaitEventType { get; init; }

        public string? WaitEvent { get; init; }

        public string Query { get; init; } = string.Empty;
    }

    private static Task<T> RunOnOwnThread<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work().GetAwaiter().GetResult());
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        return completion.Task;
    }

    private static async Task<InventoryRuleException?> WrapAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (InventoryRuleException exception)
        {
            return exception;
        }
    }
}

public sealed class InventoryDatabaseFixture : IAsyncLifetime
{
    public NpgsqlConnectionFactory Connections { get; private set; } = null!;

    public SqlMigrationRunner Runner { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var shop = ReadShopConnectionString();
        var builder = new NpgsqlConnectionStringBuilder(shop)
        {
            Database = "postgres"
        };
        await EnsureDatabaseAsync(builder.ConnectionString);
        builder.Database = InventoryDatabaseGuard.PermittedDatabase;
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            InventoryDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
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
        InventoryDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
    }

    public InventoryGraph CreateGraph()
    {
        var transactions = new PostgresTransaction(Connections);
        var ledger = new StockLedgerStore(Connections, transactions);
        var areas = new AreaInventoryStore(Connections, transactions);
        var shelves = new ShelfInventoryStore(Connections, transactions);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        return new InventoryGraph(
            Connections,
            transactions,
            ledger,
            new AreaService(areas, clock),
            new ShelfService(shelves, areas, clock),
            new StockService(ledger, ledger, shelves, clock));
    }

    public async Task ResetAsync()
    {
        await using var connection = await OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        InventoryDatabaseGuard.Require(name);
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            """
            alter table stock_movements disable trigger stock_movements_append_only;
            delete from stock_movements;
            alter table stock_movements enable trigger stock_movements_append_only;
            delete from stock_balances;
            delete from stock_operations;
            delete from shelves;
            delete from areas;
            delete from product_barcodes;
            delete from products;
            delete from units;
            delete from categories;
            delete from brands;
            """,
            transaction: transaction);
        await transaction.CommitAsync();
        InventoryDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
    }

    private static async Task EnsureDatabaseAsync(string adminConnectionString)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "select exists(select 1 from pg_database where datname = @Name);",
            new { Name = InventoryDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {InventoryDatabaseGuard.PermittedDatabase};");
    }

    private static string ReadShopConnectionString()
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

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}

public sealed class InventoryGraph
{
    private readonly NpgsqlConnectionFactory _connections;

    public InventoryGraph(
        NpgsqlConnectionFactory connections,
        PostgresTransaction transactions,
        StockLedgerStore ledger,
        AreaService areas,
        ShelfService shelves,
        StockService stock)
    {
        _connections = connections;
        Transactions = transactions;
        Ledger = ledger;
        Areas = areas;
        Shelves = shelves;
        Stock = stock;
    }

    public PostgresTransaction Transactions { get; }

    public StockLedgerStore Ledger { get; }

    public AreaService Areas { get; }

    public ShelfService Shelves { get; }

    public StockService Stock { get; }

    public async Task<SeededStock> SeedProductAndShelfAsync(
        string productName,
        decimal minimumStock,
        string areaName,
        string shelfName)
    {
        var area = await Areas.CreateAsync(new SaveAreaCommand(areaName), CancellationToken.None);
        var shelf = await Shelves.CreateAsync(new SaveShelfCommand(area.Id, shelfName), CancellationToken.None);
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var connection = await _connections.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            """
            insert into units (id, name, created_at, updated_at, is_deleted)
            values (@Id, @Name, now(), now(), false);
            insert into products (
                id, name, category_id, brand_id, unit_id,
                purchase_price, selling_price, minimum_stock, is_active,
                created_at, updated_at, is_deleted)
            values (
                @ProductId, @ProductName, null, null, @Id,
                1.00, 2.00, @MinimumStock, true,
                now(), now(), false);
            """,
            new { Id = unitId, Name = $"Unit {unitId:N}", ProductId = productId, ProductName = productName, MinimumStock = minimumStock });
        return new SeededStock(area.Id, shelf.Id, productId);
    }

    public async Task SetProductActiveAsync(Guid productId, bool isActive)
    {
        await using var connection = await _connections.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            "update products set is_active = @IsActive, updated_at = now() where id = @Id;",
            new { Id = productId, IsActive = isActive });
    }

    public async Task SetProductDeletedAsync(Guid productId)
    {
        await using var connection = await _connections.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            "update products set is_deleted = true, updated_at = now() where id = @Id;",
            new { Id = productId });
    }
}

public sealed record SeededStock(Guid AreaId, Guid ShelfId, Guid ProductId);
