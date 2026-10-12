using Retail360.Application.Catalog;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;
using Xunit;

namespace Retail360.Tests;

public class InventoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Opening_stock_posts_one_inbound_movement()
    {
        var fixture = new InventoryFixture();
        var area = await fixture.Areas.CreateAsync(new SaveAreaCommand("Front"), CancellationToken.None);
        var shelf = await fixture.Shelves.CreateAsync(new SaveShelfCommand(area.Id, "A1"), CancellationToken.None);

        var movements = await fixture.Stock.OpenAsync(
            new OpeningStockCommand(fixture.ProductId, shelf.Id, 2.500m, " opening ", Guid.NewGuid()),
            CancellationToken.None);

        var movement = Assert.Single(movements);
        Assert.Equal("in", movement.Direction);
        Assert.Equal("opening", movement.MovementType);
        Assert.Equal(2.500m, movement.Quantity);
        Assert.Equal("opening", movement.Reason);
    }

    [Fact]
    public async Task Opening_stock_rejects_an_inactive_shelf_before_posting()
    {
        var fixture = new InventoryFixture();
        var area = await fixture.Areas.CreateAsync(new SaveAreaCommand("Front"), CancellationToken.None);
        var shelf = await fixture.Shelves.CreateAsync(new SaveShelfCommand(area.Id, "A1"), CancellationToken.None);
        await fixture.Shelves.DeactivateAsync(shelf.Id, CancellationToken.None);

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Stock.OpenAsync(
                new OpeningStockCommand(fixture.ProductId, shelf.Id, 1m, "opening", Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, error.Failure);
        Assert.Empty(fixture.Ledger.Postings);
    }

    [Fact]
    public async Task Opening_stock_requires_an_operation_id()
    {
        var fixture = new InventoryFixture();
        var area = await fixture.Areas.CreateAsync(new SaveAreaCommand("Front"), CancellationToken.None);
        var shelf = await fixture.Shelves.CreateAsync(new SaveShelfCommand(area.Id, "A1"), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Stock.OpenAsync(
                new OpeningStockCommand(fixture.ProductId, shelf.Id, 1m, "opening", Guid.Empty),
                CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Invalid, error.Failure);
        Assert.Empty(fixture.Ledger.Postings);
    }

    [Fact]
    public async Task Creating_a_shelf_for_a_missing_area_is_not_found()
    {
        var fixture = new InventoryFixture();

        var error = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Shelves.CreateAsync(new SaveShelfCommand(Guid.NewGuid(), "A1"), CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.NotFound, error.Failure);
    }

    [Fact]
    public async Task Adjustment_uses_the_sign_as_direction()
    {
        var fixture = new InventoryFixture();
        var shelf = await fixture.CreateShelfAsync("A1");

        var increase = await fixture.Stock.AdjustAsync(
            new AdjustmentCommand(fixture.ProductId, shelf.Id, 1.5m, "count", Guid.NewGuid()),
            CancellationToken.None);
        var decrease = await fixture.Stock.AdjustAsync(
            new AdjustmentCommand(fixture.ProductId, shelf.Id, -0.5m, InventoryText.Damaged, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal("in", Assert.Single(increase).Direction);
        Assert.Equal("out", Assert.Single(decrease).Direction);
        Assert.Equal("damaged", Assert.Single(decrease).Reason);
    }

    [Fact]
    public async Task Inactive_product_can_be_adjusted_and_a_deleted_product_cannot()
    {
        var fixture = new InventoryFixture();
        var shelf = await fixture.CreateShelfAsync("A1");
        fixture.Products.Product = fixture.Products.Product! with { IsActive = false };

        var adjusted = await fixture.Stock.AdjustAsync(
            new AdjustmentCommand(fixture.ProductId, shelf.Id, -1m, InventoryText.Expired, Guid.NewGuid()),
            CancellationToken.None);
        fixture.Products.Product = null;
        var missing = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Stock.AdjustAsync(
                new AdjustmentCommand(fixture.ProductId, shelf.Id, -1m, InventoryText.Missing, Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal("out", Assert.Single(adjusted).Direction);
        Assert.Equal(InventoryRuleFailure.NotFound, missing.Failure);
    }

    [Fact]
    public async Task Inactive_shelf_blocks_inbound_adjustment_and_allows_outbound()
    {
        var fixture = new InventoryFixture();
        var shelf = await fixture.CreateShelfAsync("A1");
        await fixture.Shelves.DeactivateAsync(shelf.Id, CancellationToken.None);

        var inbound = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Stock.AdjustAsync(
                new AdjustmentCommand(fixture.ProductId, shelf.Id, 1m, "count", Guid.NewGuid()),
                CancellationToken.None));
        var outbound = await fixture.Stock.AdjustAsync(
            new AdjustmentCommand(fixture.ProductId, shelf.Id, -1m, InventoryText.Missing, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(InventoryRuleFailure.Conflict, inbound.Failure);
        Assert.Equal("out", Assert.Single(outbound).Direction);
    }

    [Fact]
    public async Task Transfer_rejects_the_same_shelf_and_an_inactive_destination()
    {
        var fixture = new InventoryFixture();
        var source = await fixture.CreateShelfAsync("A1");
        var destination = await fixture.CreateShelfAsync("B1");
        await fixture.Shelves.DeactivateAsync(destination.Id, CancellationToken.None);

        var same = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Stock.TransferAsync(
                new TransferCommand(fixture.ProductId, source.Id, source.Id, 1m, null, Guid.NewGuid()),
                CancellationToken.None));
        var inactive = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Stock.TransferAsync(
                new TransferCommand(fixture.ProductId, source.Id, destination.Id, 1m, null, Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Invalid, same.Failure);
        Assert.Equal(InventoryRuleFailure.Conflict, inactive.Failure);
        Assert.Empty(fixture.Ledger.Postings);
    }

    [Fact]
    public async Task Transfer_from_an_inactive_source_posts_both_sides()
    {
        var fixture = new InventoryFixture();
        var source = await fixture.CreateShelfAsync("A1");
        var destination = await fixture.CreateShelfAsync("B1");
        await fixture.Shelves.DeactivateAsync(source.Id, CancellationToken.None);

        var movements = await fixture.Stock.TransferAsync(
            new TransferCommand(fixture.ProductId, source.Id, destination.Id, 1.250m, "move", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, movement => movement.ShelfId == source.Id && movement.Direction == "out");
        Assert.Contains(movements, movement => movement.ShelfId == destination.Id && movement.Direction == "in");
        Assert.All(movements, movement => Assert.Equal(movements[0].OperationId, movement.OperationId));
    }

    [Fact]
    public async Task Delete_is_blocked_when_stock_history_exists()
    {
        var fixture = new InventoryFixture();
        var area = await fixture.Areas.CreateAsync(new SaveAreaCommand("Front"), CancellationToken.None);
        var shelf = await fixture.Shelves.CreateAsync(new SaveShelfCommand(area.Id, "A1"), CancellationToken.None);
        fixture.ShelvesStore.History.Add(shelf.Id);
        fixture.AreasStore.Blocked.Add(area.Id);

        var shelfError = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Shelves.DeleteAsync(shelf.Id, CancellationToken.None));
        var areaError = await Assert.ThrowsAsync<InventoryRuleException>(() =>
            fixture.Areas.DeleteAsync(area.Id, CancellationToken.None));

        Assert.Equal(InventoryRuleFailure.Conflict, shelfError.Failure);
        Assert.Equal(InventoryRuleFailure.Conflict, areaError.Failure);
        Assert.False((await fixture.ShelvesStore.FindAsync(shelf.Id, CancellationToken.None))!.IsDeleted);
    }

    [Fact]
    public async Task Empty_area_and_shelf_can_be_soft_deleted()
    {
        var fixture = new InventoryFixture();
        var area = await fixture.Areas.CreateAsync(new SaveAreaCommand("Front"), CancellationToken.None);
        var shelf = await fixture.Shelves.CreateAsync(new SaveShelfCommand(area.Id, "A1"), CancellationToken.None);

        await fixture.Shelves.DeleteAsync(shelf.Id, CancellationToken.None);
        await fixture.Areas.DeleteAsync(area.Id, CancellationToken.None);

        await Assert.ThrowsAsync<InventoryRuleException>(() => fixture.Areas.GetAsync(area.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InventoryRuleException>(() => fixture.Shelves.GetAsync(shelf.Id, CancellationToken.None));
    }

    private sealed class InventoryFixture
    {
        public InventoryFixture()
        {
            AreasStore = new FakeAreas();
            ShelvesStore = new FakeShelves(AreasStore);
            Products = new FakeProducts();
            Ledger = new FakeLedger();
            var clock = new FixedClock(Now);
            Areas = new AreaService(AreasStore, clock);
            Shelves = new ShelfService(ShelvesStore, AreasStore, clock);
            Stock = new StockService(Ledger, Products, ShelvesStore, clock);
            ProductId = Guid.NewGuid();
            Products.Product = new InventoryProduct(ProductId, "Rice", 1.000m, true);
        }

        public FakeAreas AreasStore { get; }

        public FakeShelves ShelvesStore { get; }

        public FakeProducts Products { get; }

        public FakeLedger Ledger { get; }

        public AreaService Areas { get; }

        public ShelfService Shelves { get; }

        public StockService Stock { get; }

        public Guid ProductId { get; }

        public async Task<ShelfResponse> CreateShelfAsync(string name)
        {
            var area = await Areas.CreateAsync(new SaveAreaCommand($"Area {name}"), CancellationToken.None);
            return await Shelves.CreateAsync(new SaveShelfCommand(area.Id, name), CancellationToken.None);
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeAreas : IAreaStore
    {
        private readonly List<Area> _areas = [];

        public HashSet<Guid> Blocked { get; } = [];

        public Task<Area?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_areas.SingleOrDefault(area => area.Id == id && !area.IsDeleted));
        }

        public Task<IReadOnlyList<Area>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Area>>(_areas.Where(area => !area.IsDeleted).ToArray());
        }

        public Task<bool> HasShelfOrHistoryAsync(Guid areaId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Blocked.Contains(areaId));
        }

        public Task SoftDeleteIfNoShelfOrHistoryAsync(Guid areaId, DateTimeOffset deletedAt, CancellationToken cancellationToken)
        {
            var area = _areas.SingleOrDefault(item => item.Id == areaId && !item.IsDeleted)
                ?? throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Area was not found.");
            if (Blocked.Contains(areaId))
            {
                throw new InventoryRuleException(
                    InventoryRuleFailure.Conflict,
                    "Area cannot be deleted because it has a shelf or stock history.");
            }

            area.SoftDelete(deletedAt);
            return Task.CompletedTask;
        }

        public Task AddAsync(Area area, CancellationToken cancellationToken)
        {
            _areas.Add(area);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Area area, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeShelves : IShelfStore
    {
        private readonly List<Shelf> _shelves = [];
        private readonly FakeAreas _areas;

        public FakeShelves(FakeAreas areas)
        {
            _areas = areas;
        }

        public HashSet<Guid> History { get; } = [];

        public Task<Shelf?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_shelves.SingleOrDefault(shelf => shelf.Id == id && !shelf.IsDeleted));
        }

        public async Task<ShelfResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            var shelf = await FindAsync(id, cancellationToken);
            if (shelf is null)
            {
                return null;
            }

            var area = await _areas.FindAsync(shelf.AreaId, cancellationToken);
            return new ShelfResponse(shelf.Id, shelf.AreaId, area?.Name ?? string.Empty, shelf.Name, shelf.IsActive, shelf.CreatedAt, shelf.UpdatedAt);
        }

        public async Task<IReadOnlyList<ShelfResponse>> ListAsync(Guid? areaId, CancellationToken cancellationToken)
        {
            var shelves = _shelves.Where(shelf => !shelf.IsDeleted && (areaId is null || shelf.AreaId == areaId));
            var responses = new List<ShelfResponse>();
            foreach (var shelf in shelves)
            {
                responses.Add((await GetAsync(shelf.Id, cancellationToken))!);
            }

            return responses;
        }

        public Task<bool> HasHistoryAsync(Guid shelfId, CancellationToken cancellationToken)
        {
            return Task.FromResult(History.Contains(shelfId));
        }

        public async Task SoftDeleteIfNoHistoryAsync(Guid shelfId, DateTimeOffset deletedAt, CancellationToken cancellationToken)
        {
            var shelf = await FindAsync(shelfId, cancellationToken)
                ?? throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf was not found.");
            if (History.Contains(shelfId))
            {
                throw new InventoryRuleException(
                    InventoryRuleFailure.Conflict,
                    "Shelf cannot be deleted because it has stock history.");
            }

            shelf.SoftDelete(deletedAt);
        }

        public Task AddAsync(Shelf shelf, CancellationToken cancellationToken)
        {
            _shelves.Add(shelf);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Shelf shelf, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProducts : IInventoryProductLookup
    {
        public InventoryProduct? Product { get; set; }

        public Task<InventoryProduct?> FindAvailableAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Product?.Id == id ? Product : null);
        }
    }

    private sealed class FakeLedger : IStockLedger
    {
        public List<StockPosting> Postings { get; } = [];

        public Task<IReadOnlyList<StockMovementResponse>> PostAsync(StockPosting posting, CancellationToken cancellationToken)
        {
            Postings.Add(posting);
            IReadOnlyList<StockMovementResponse> movements = posting.Lines.Select(line => new StockMovementResponse(
                Guid.NewGuid(),
                posting.OperationId,
                posting.ProductId,
                line.ShelfId,
                line.Quantity,
                StockCodes.ToCode(line.Direction),
                StockCodes.ToCode(line.MovementType),
                line.Reason,
                line.OtherShelfId,
                posting.OccurredAt)).ToArray();
            return Task.FromResult(movements);
        }

        public Task<IReadOnlyList<ShelfBalanceResponse>> ListBalancesAsync(Guid? productId, Guid? shelfId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ShelfBalanceResponse>>([]);
        }

        public Task<IReadOnlyList<StockMovementResponse>> ListMovementsAsync(Guid? productId, Guid? shelfId, int limit, int offset, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<StockMovementResponse>>([]);
        }

        public Task<IReadOnlyList<ProductStockResponse>> ListProductStockAsync(string? status, int limit, int offset, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ProductStockResponse>>([]);
        }

        public Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReconciliationRow>>([]);
        }
    }
}
