using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Retail360.Application.Identity;
using Retail360.Domain.Audit;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("audit-database", DisableParallelization = true)]
public sealed class AuditDatabaseCollection : ICollectionFixture<AuditDatabaseFixture>
{
}

[Collection("audit-database")]
public sealed class AuditDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly AuditDatabaseFixture _database;

    public AuditDatabaseTests(AuditDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_shop_and_other_test_databases_without_connecting()
    {
        Assert.Equal("retail360_audit_test", AuditDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_identity_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_suppliers_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_purchasing_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_sales_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_expenses_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_reports_test"));
        Assert.Throws<InvalidOperationException>(() => AuditDatabaseGuard.Require("retail360_users_test"));
    }

    [Fact]
    public async Task Migration_creates_an_append_only_audit_table_once()
    {
        await using var connection = await _database.OpenAsync();
        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations where version = '012_audit_log';")).ToArray();
        var columns = (await connection.QueryAsync<string>(
            """
            select column_name
            from information_schema.columns
            where table_schema = 'public' and table_name = 'audit_entries'
            order by ordinal_position;
            """)).ToArray();

        Assert.Equal(["012_audit_log"], versions);
        Assert.Equal(
            ["id", "occurred_at", "actor_user_id", "action", "entity_type", "entity_id", "details"],
            columns);
        Assert.DoesNotContain("is_deleted", columns);
    }

    [Fact]
    public async Task Successful_actions_are_attributed_and_failed_ones_leave_no_audit()
    {
        await _database.ResetAsync();
        var adminId = await _database.CreateUserAsync("owner", ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var admin = await LoginAsync(client, "owner", Password);
        Assert.Equal(adminId, admin.UserId);
        var tokens = new List<string> { admin.Token };

        var stockerId = await CreateUserAsync(client, admin.Token, "stocker", ShopRoleNames.InventoryStaff);
        var cashierId = await CreateUserAsync(client, admin.Token, "cashier", ShopRoleNames.Cashier);
        var managerId = await CreateUserAsync(client, admin.Token, "boss", ShopRoleNames.Manager);
        var counterId = await CreateUserAsync(client, admin.Token, "counter", ShopRoleNames.Cashier);
        var stocker = await LoginAsync(client, "stocker", Password);
        var cashier = await LoginAsync(client, "cashier", Password);
        var manager = await LoginAsync(client, "boss", Password);
        tokens.AddRange([stocker.Token, cashier.Token, manager.Token]);

        using (var anonymous = await client.GetAsync("/api/audit"))
        using (var cashierDenied = await SendAsync(client, HttpMethod.Get, "/api/audit", cashier.Token))
        using (var managerDenied = await SendAsync(client, HttpMethod.Get, "/api/audit", manager.Token))
        using (var stockerDenied = await SendAsync(client, HttpMethod.Get, "/api/audit", stocker.Token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, cashierDenied.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, managerDenied.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, stockerDenied.StatusCode);
        }

        await AssertUnchanged(async () =>
        {
            using var missingUnit = await SendAsync(client, HttpMethod.Post, "/api/products", admin.Token, new
            {
                name = "Missing",
                unitId = Guid.NewGuid(),
                purchasePrice = 1.00m,
                sellingPrice = 2.00m,
                minimumStock = 0m,
                barcodes = Array.Empty<string>()
            });
            Assert.Equal(HttpStatusCode.NotFound, missingUnit.StatusCode);
        });

        var unit = await SendOkAsync(client, HttpMethod.Post, "/api/units", admin.Token, new { name = "Piece" });
        var unitId = unit.GetProperty("id").GetGuid();
        var rice = await SendOkAsync(client, HttpMethod.Post, "/api/products", admin.Token, ProductBody("Rice", unitId));
        var riceId = rice.GetProperty("id").GetGuid();
        var area = await SendOkAsync(client, HttpMethod.Post, "/api/areas", admin.Token, new { name = "Shop" });
        var areaId = area.GetProperty("id").GetGuid();
        var shelfA = await SendOkAsync(client, HttpMethod.Post, "/api/shelves", admin.Token, new { areaId, name = "A" });
        var shelfB = await SendOkAsync(client, HttpMethod.Post, "/api/shelves", admin.Token, new { areaId, name = "B" });
        var shelfAId = shelfA.GetProperty("id").GetGuid();
        var shelfBId = shelfB.GetProperty("id").GetGuid();

        var openingId = Guid.NewGuid();
        var opening = new { productId = riceId, shelfId = shelfAId, quantity = 5m, reason = "count", operationId = openingId };
        await SendOkAsync(client, HttpMethod.Post, "/api/inventory/opening", stocker.Token, opening);
        await AssertActionUnchanged(AuditActions.StockOpening, () =>
            SendOkAsync(client, HttpMethod.Post, "/api/inventory/opening", admin.Token, opening));
        await AssertUnchanged(async () =>
        {
            using var conflict = await SendAsync(client, HttpMethod.Post, "/api/inventory/opening", admin.Token, new
            {
                productId = riceId,
                shelfId = shelfAId,
                quantity = 4m,
                reason = "count",
                operationId = openingId
            });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        });

        var adjustmentId = Guid.NewGuid();
        var adjustment = new
        {
            productId = riceId,
            shelfId = shelfAId,
            quantity = -1m,
            reason = "damaged",
            operationId = adjustmentId
        };
        await SendOkAsync(client, HttpMethod.Post, "/api/inventory/adjustments", admin.Token, adjustment);
        await AssertActionUnchanged(AuditActions.StockAdjustment, () =>
            SendOkAsync(client, HttpMethod.Post, "/api/inventory/adjustments", admin.Token, adjustment));
        await AssertUnchanged(async () =>
        {
            using var overdraw = await SendAsync(client, HttpMethod.Post, "/api/inventory/adjustments", admin.Token, new
            {
                productId = riceId,
                shelfId = shelfAId,
                quantity = -100m,
                reason = "missing",
                operationId = Guid.NewGuid()
            });
            Assert.Equal(HttpStatusCode.Conflict, overdraw.StatusCode);
        });

        var transferId = Guid.NewGuid();
        var transfer = new
        {
            productId = riceId,
            sourceShelfId = shelfAId,
            destinationShelfId = shelfBId,
            quantity = 1m,
            reason = "move",
            operationId = transferId
        };
        await SendOkAsync(client, HttpMethod.Post, "/api/inventory/transfers", admin.Token, transfer);
        await AssertActionUnchanged(AuditActions.StockTransfer, () =>
            SendOkAsync(client, HttpMethod.Post, "/api/inventory/transfers", admin.Token, transfer));

        var saleId = Guid.NewGuid();
        var sale = new
        {
            operationId = saleId,
            lines = new[] { new { productId = riceId, shelfId = shelfAId, quantity = 1m } }
        };
        var completed = await SendOkAsync(client, HttpMethod.Post, "/api/sales", cashier.Token, sale);
        var completedSaleId = completed.GetProperty("id").GetGuid();
        await AssertActionUnchanged(AuditActions.SaleCompleted, () =>
            SendOkAsync(client, HttpMethod.Post, "/api/sales", cashier.Token, sale));
        await AssertUnchanged(async () =>
        {
            using var differentSale = await SendAsync(client, HttpMethod.Post, "/api/sales", cashier.Token, new
            {
                operationId = saleId,
                lines = new[] { new { productId = riceId, shelfId = shelfAId, quantity = 2m } }
            });
            Assert.Equal(HttpStatusCode.Conflict, differentSale.StatusCode);
        });

        var soap = await SendOkAsync(client, HttpMethod.Post, "/api/products", admin.Token, ProductBody("Soap", unitId));
        var soapId = soap.GetProperty("id").GetGuid();
        await SendOkAsync(client, HttpMethod.Put, $"/api/products/{soapId}", admin.Token, ProductBody("Soap bar", unitId));
        using (var removedSoap = await SendAsync(client, HttpMethod.Delete, $"/api/products/{soapId}", admin.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, removedSoap.StatusCode);
        }

        await AssertUnchanged(async () =>
        {
            using var blocked = await SendAsync(client, HttpMethod.Delete, $"/api/products/{riceId}", admin.Token);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        });

        var mill = await SendOkAsync(client, HttpMethod.Post, "/api/suppliers", admin.Token, SupplierBody("Mill"));
        var millId = mill.GetProperty("id").GetGuid();
        await SendOkAsync(client, HttpMethod.Put, $"/api/suppliers/{millId}", admin.Token, SupplierBody("Mill Co"));
        await SendOkAsync(client, HttpMethod.Post, $"/api/suppliers/{millId}/deactivate", admin.Token);
        await SendOkAsync(client, HttpMethod.Post, $"/api/suppliers/{millId}/activate", admin.Token);
        var purchase = await SendOkAsync(client, HttpMethod.Post, "/api/purchases", admin.Token, new
        {
            supplierId = millId,
            purchaseDate = "2026-10-31",
            reference = (string?)null,
            notes = (string?)null,
            lines = new[] { new { productId = riceId, quantity = 1m, unitCost = 2.50m } }
        });
        var purchaseId = purchase.GetProperty("id").GetGuid();
        var lineId = purchase.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var receiveId = Guid.NewGuid();
        var receive = new { operationId = receiveId, lines = new[] { new { lineId, shelfId = shelfBId } } };
        await SendOkAsync(client, HttpMethod.Post, $"/api/purchases/{purchaseId}/receive", admin.Token, receive);
        await AssertActionUnchanged(AuditActions.PurchaseReceived, () =>
            SendOkAsync(client, HttpMethod.Post, $"/api/purchases/{purchaseId}/receive", admin.Token, receive));
        await AssertUnchanged(async () =>
        {
            using var blockedSupplier = await SendAsync(client, HttpMethod.Delete, $"/api/suppliers/{millId}", admin.Token);
            Assert.Equal(HttpStatusCode.Conflict, blockedSupplier.StatusCode);
        });
        var farm = await SendOkAsync(client, HttpMethod.Post, "/api/suppliers", admin.Token, SupplierBody("Farm"));
        var farmId = farm.GetProperty("id").GetGuid();
        using (var removedFarm = await SendAsync(client, HttpMethod.Delete, $"/api/suppliers/{farmId}", admin.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, removedFarm.StatusCode);
        }

        var expenseOperationId = Guid.NewGuid();
        var expenseBody = new
        {
            operationId = expenseOperationId,
            category = "rent",
            amount = 12.50m,
            expenseDate = "2026-10-31",
            notes = "office key",
            paymentMethod = "cash"
        };
        var expense = await SendOkAsync(client, HttpMethod.Post, "/api/expenses", admin.Token, expenseBody);
        var expenseId = expense.GetProperty("id").GetGuid();
        await AssertActionUnchanged(AuditActions.ExpenseCreated, () =>
            SendOkAsync(client, HttpMethod.Post, "/api/expenses", admin.Token, expenseBody));
        await SendOkAsync(client, HttpMethod.Put, $"/api/expenses/{expenseId}", admin.Token, new
        {
            category = "utilities",
            amount = 15.00m,
            expenseDate = "2026-10-30",
            notes = "office key",
            paymentMethod = "cash"
        });
        using (var removedExpense = await SendAsync(client, HttpMethod.Delete, $"/api/expenses/{expenseId}", admin.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, removedExpense.StatusCode);
        }

        await AssertUnchanged(async () =>
        {
            using var duplicate = await SendAsync(client, HttpMethod.Post, "/api/users", admin.Token, new
            {
                userName = "counter",
                role = ShopRoleNames.Cashier,
                password = Password
            });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        });
        await SendOkAsync(client, HttpMethod.Put, $"/api/users/{counterId}", admin.Token, new
        {
            userName = "counter",
            role = ShopRoleNames.Manager,
            password = ""
        });
        await AssertActionUnchanged(AuditActions.UserRoleChanged, () =>
            SendOkAsync(client, HttpMethod.Put, $"/api/users/{counterId}", admin.Token, new
            {
                userName = "counter-two",
                role = ShopRoleNames.Manager,
                password = "another-horse"
            }));
        await SendOkAsync(client, HttpMethod.Post, $"/api/users/{counterId}/deactivate", admin.Token);
        await SendOkAsync(client, HttpMethod.Post, $"/api/users/{counterId}/activate", admin.Token);
        using (var removedCounter = await SendAsync(client, HttpMethod.Delete, $"/api/users/{counterId}", admin.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, removedCounter.StatusCode);
        }

        await AssertUnchanged(async () =>
        {
            using var lastAdmin = await SendAsync(client, HttpMethod.Put, $"/api/users/{adminId}", admin.Token, new
            {
                userName = "owner",
                role = ShopRoleNames.Cashier,
                password = ""
            });
            Assert.Equal(HttpStatusCode.Conflict, lastAdmin.StatusCode);
        });
        using (var removedStocker = await SendAsync(client, HttpMethod.Delete, $"/api/users/{stockerId}", admin.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, removedStocker.StatusCode);
        }

        using var posted = await SendAsync(client, HttpMethod.Post, "/api/audit", admin.Token, new { action = "user.created" });
        using var edited = await SendAsync(client, HttpMethod.Put, "/api/audit", admin.Token, new { action = "user.created" });
        using var removed = await SendAsync(client, HttpMethod.Delete, $"/api/audit/{Guid.NewGuid()}", admin.Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, posted.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);

        Assert.Equal(4, await CountAsync(AuditActions.UserCreated));
        Assert.Equal(1, await CountAsync(AuditActions.UserRoleChanged));
        Assert.Equal(1, await CountAsync(AuditActions.UserActivated));
        Assert.Equal(1, await CountAsync(AuditActions.UserDeactivated));
        Assert.Equal(2, await CountAsync(AuditActions.UserDeleted));
        Assert.Equal(2, await CountAsync(AuditActions.ProductCreated));
        Assert.Equal(1, await CountAsync(AuditActions.ProductUpdated));
        Assert.Equal(1, await CountAsync(AuditActions.ProductDeleted));
        Assert.Equal(1, await CountAsync(AuditActions.StockOpening));
        Assert.Equal(1, await CountAsync(AuditActions.StockAdjustment));
        Assert.Equal(1, await CountAsync(AuditActions.StockTransfer));
        Assert.Equal(1, await CountAsync(AuditActions.PurchaseCreated));
        Assert.Equal(1, await CountAsync(AuditActions.PurchaseReceived));
        Assert.Equal(1, await CountAsync(AuditActions.SaleCompleted));
        Assert.Equal(1, await CountAsync(AuditActions.ExpenseCreated));
        Assert.Equal(1, await CountAsync(AuditActions.ExpenseUpdated));
        Assert.Equal(1, await CountAsync(AuditActions.ExpenseDeleted));
        Assert.Equal(2, await CountAsync(AuditActions.SupplierCreated));
        Assert.Equal(1, await CountAsync(AuditActions.SupplierUpdated));
        Assert.Equal(1, await CountAsync(AuditActions.SupplierActivated));
        Assert.Equal(1, await CountAsync(AuditActions.SupplierDeactivated));
        Assert.Equal(1, await CountAsync(AuditActions.SupplierDeleted));
        Assert.Equal(28, await CountAsync());
        Assert.Equal(0, await CountAsync(actorMissing: true));

        Assert.Equal(adminId, await ActorAsync(AuditActions.ProductCreated, riceId.ToString("D")));
        Assert.Equal(stockerId, await ActorAsync(AuditActions.StockOpening, openingId.ToString("D")));
        Assert.Equal(cashierId, await ActorAsync(AuditActions.SaleCompleted, completedSaleId.ToString("D")));
        Assert.Equal(adminId, await ActorAsync(AuditActions.UserCreated, counterId));
        Assert.Equal(adminId, await ActorAsync(AuditActions.PurchaseReceived, purchaseId.ToString("D")));
        Assert.Contains("\"total\": \"2.50\"", await DetailsAsync(AuditActions.PurchaseCreated, purchaseId.ToString("D")), StringComparison.Ordinal);
        Assert.Contains("\"quantity\": \"5.000\"", await DetailsAsync(AuditActions.StockOpening, openingId.ToString("D")), StringComparison.Ordinal);
        Assert.Contains("\"previousRole\": \"Cashier\"", await DetailsAsync(AuditActions.UserRoleChanged, counterId), StringComparison.Ordinal);
        Assert.Contains("\"role\": \"Manager\"", await DetailsAsync(AuditActions.UserRoleChanged, counterId), StringComparison.Ordinal);
        Assert.DoesNotContain("office key", await DetailsAsync(AuditActions.ExpenseCreated, expenseId.ToString("D")), StringComparison.Ordinal);

        var history = await SendOkAsync(client, HttpMethod.Get, "/api/audit?limit=200&offset=0", admin.Token);
        var historyText = history.GetRawText();
        Assert.Contains(soapId.ToString("D"), historyText, StringComparison.Ordinal);
        Assert.Contains(counterId, historyText, StringComparison.Ordinal);
        Assert.Contains(farmId.ToString("D"), historyText, StringComparison.Ordinal);
        Assert.Contains("stocker", historyText, StringComparison.Ordinal);
        Assert.Equal(28, history.GetArrayLength());
        foreach (var item in history.EnumerateArray())
        {
            foreach (var property in item.GetProperty("details").EnumerateObject())
            {
                Assert.DoesNotContain("password", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("token", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secret", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hash", property.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        var stored = await AllDetailsAsync();
        Assert.DoesNotContain(Password, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("another-horse", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, historyText, StringComparison.Ordinal);
        Assert.DoesNotContain("another-horse", historyText, StringComparison.Ordinal);
        foreach (var token in tokens)
        {
            Assert.DoesNotContain(token, stored, StringComparison.Ordinal);
            Assert.DoesNotContain(token, historyText, StringComparison.Ordinal);
        }

        await using var connection = await _database.OpenAsync();
        var hashes = await connection.QueryAsync<string>("select password_hash from asp_net_users;");
        foreach (var hash in hashes)
        {
            Assert.False(string.IsNullOrWhiteSpace(hash));
            Assert.DoesNotContain(hash, stored, StringComparison.Ordinal);
            Assert.DoesNotContain(hash, historyText, StringComparison.Ordinal);
        }

        Assert.NotEqual(adminId, managerId);
    }

    [Fact]
    public async Task Filters_use_inclusive_utc_dates_and_pages()
    {
        await _database.ResetAsync();
        await _database.CreateUserAsync("owner", ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var admin = await LoginAsync(client, "owner", Password);
        await _database.InsertAuditAsync(new DateTimeOffset(2026, 10, 21, 23, 59, 59, TimeSpan.Zero), "day-21");
        await _database.InsertAuditAsync(new DateTimeOffset(2026, 10, 22, 0, 30, 0, TimeSpan.Zero), "day-22");
        await _database.InsertAuditAsync(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), "day-23");

        var first = await SendOkAsync(
            client,
            HttpMethod.Get,
            "/api/audit?actorUserId=boundary-actor&limit=1&offset=0",
            admin.Token);
        var second = await SendOkAsync(
            client,
            HttpMethod.Get,
            "/api/audit?actorUserId=boundary-actor&limit=1&offset=1",
            admin.Token);
        var day = await SendOkAsync(
            client,
            HttpMethod.Get,
            "/api/audit?actorUserId=boundary-actor&from=2026-10-22&to=2026-10-22",
            admin.Token);
        var before = await SendOkAsync(
            client,
            HttpMethod.Get,
            "/api/audit?actorUserId=boundary-actor&from=2026-10-21&to=2026-10-21",
            admin.Token);
        var after = await SendOkAsync(
            client,
            HttpMethod.Get,
            "/api/audit?actorUserId=boundary-actor&from=2026-10-23&to=2026-10-23",
            admin.Token);
        var empty = await SendOkAsync(
            client,
            HttpMethod.Get,
            "/api/audit?actorUserId=boundary-actor&from=2026-10-24&to=2026-10-24",
            admin.Token);
        using var reversed = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/audit?from=2026-10-23&to=2026-10-22",
            admin.Token);
        using var unknown = await SendAsync(client, HttpMethod.Get, "/api/audit?action=nope", admin.Token);
        using var badLimit = await SendAsync(client, HttpMethod.Get, "/api/audit?limit=0", admin.Token);

        Assert.Equal("day-23", first[0].GetProperty("entityId").GetString());
        Assert.Equal("day-22", second[0].GetProperty("entityId").GetString());
        Assert.Equal(1, day.GetArrayLength());
        Assert.Equal("day-22", day[0].GetProperty("entityId").GetString());
        Assert.Equal("day-21", before[0].GetProperty("entityId").GetString());
        Assert.Equal("day-23", after[0].GetProperty("entityId").GetString());
        Assert.Equal(0, empty.GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
        Assert.Equal(3, await CountAsync());
    }

    private async Task AssertUnchanged(Func<Task> action)
    {
        var before = await CountAsync();
        await action();
        Assert.Equal(before, await CountAsync());
    }

    private async Task AssertActionUnchanged(string action, Func<Task> replay)
    {
        var before = await CountAsync(action);
        await replay();
        Assert.Equal(before, await CountAsync(action));
    }

    private async Task<int> CountAsync(string? action = null, bool actorMissing = false)
    {
        await using var connection = await _database.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            select count(*)::int
            from audit_entries
            where (cast(@Action as text) is null or action = cast(@Action as text))
              and (@ActorMissing = false or actor_user_id is null);
            """,
            new { Action = action, ActorMissing = actorMissing });
    }

    private async Task<string?> ActorAsync(string action, string entityId)
    {
        await using var connection = await _database.OpenAsync();
        return await connection.ExecuteScalarAsync<string>(
            """
            select actor_user_id
            from audit_entries
            where action = @Action and entity_id = @EntityId;
            """,
            new { Action = action, EntityId = entityId });
    }

    private async Task<string> DetailsAsync(string action, string entityId)
    {
        await using var connection = await _database.OpenAsync();
        return await connection.ExecuteScalarAsync<string>(
            """
            select details::text
            from audit_entries
            where action = @Action and entity_id = @EntityId;
            """,
            new { Action = action, EntityId = entityId }) ?? string.Empty;
    }

    private async Task<string> AllDetailsAsync()
    {
        await using var connection = await _database.OpenAsync();
        return await connection.ExecuteScalarAsync<string>(
            "select coalesce(string_agg(details::text, ' '), '') from audit_entries;") ?? string.Empty;
    }

    private static async Task<string> CreateUserAsync(
        HttpClient client,
        string token,
        string userName,
        string role)
    {
        var created = await SendOkAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName,
            role,
            password = Password
        });
        return created.GetProperty("id").GetString()!;
    }

    private static object ProductBody(string name, Guid unitId)
    {
        return new
        {
            name,
            unitId,
            purchasePrice = 1.00m,
            sellingPrice = 2.00m,
            minimumStock = 0m,
            barcodes = Array.Empty<string>()
        };
    }

    private static object SupplierBody(string name)
    {
        return new
        {
            name,
            contactPerson = (string?)null,
            phone = (string?)null,
            email = (string?)null,
            address = (string?)null,
            notes = (string?)null
        };
    }

    private static async Task<(string Token, string UserId)> LoginAsync(
        HttpClient client,
        string userName,
        string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var document = JsonDocument.Parse(body);
        return (
            document.RootElement.GetProperty("accessToken").GetString()!,
            document.RootElement.GetProperty("userId").GetString()!);
    }

    private static async Task<JsonElement> SendOkAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        object? body = null)
    {
        using var response = await SendAsync(client, method, path, token, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {path} {text}");
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }
}

public static class AuditDatabaseGuard
{
    public const string PermittedDatabase = "retail360_audit_test";

    public static void Require(string? databaseName)
    {
        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated audit test database.");
        }
    }
}

public sealed class AuditDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-audit-test-signing-key";

    public const string Issuer = "retail360-audit-tests";

    public const string Audience = "retail360-audit-tests";

    private WebApplicationFactory<Program>? _factory;

    public NpgsqlConnectionFactory Connections { get; private set; } = null!;

    public ShopUserDirectory Directory { get; private set; } = null!;

    public ShopPasswordHasher Passwords { get; } = new();

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var admin = new NpgsqlConnectionStringBuilder(TestShopConnection.Read())
        {
            Database = "postgres"
        };
        await EnsureDatabaseAsync(admin.ConnectionString);
        var builder = new NpgsqlConnectionStringBuilder(TestShopConnection.Read())
        {
            Database = AuditDatabaseGuard.PermittedDatabase
        };
        AuditDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            AuditDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        var runner = new SqlMigrationRunner(Connections);
        await runner.ApplyAsync(CancellationToken.None);
        await runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new AuditApiFactory(builder.ConnectionString);
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<string> CreateUserAsync(string userName, string role)
    {
        var id = Guid.NewGuid().ToString("D");
        await Directory.CreateAsync(
            new NewShopUser(id, userName, Passwords.Hash(AuditDatabaseTestsPassword.Value), role),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        return id;
    }

    public async Task InsertAuditAsync(DateTimeOffset occurredAt, string entityId)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into audit_entries (
                id, occurred_at, actor_user_id, action, entity_type, entity_id, details)
            values (
                @Id, @OccurredAt, 'boundary-actor', 'user.created', 'user', @EntityId, '{}'::jsonb);
            """,
            new { Id = Guid.NewGuid(), OccurredAt = occurredAt, EntityId = entityId });
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        AuditDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
    }

    public async Task ResetAsync()
    {
        await using var connection = await OpenAsync();
        AuditDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        await connection.ExecuteAsync(
            """
            delete from audit_entries;
            delete from sale_lines;
            delete from sales;
            delete from purchase_lines;
            delete from purchases;
            alter table stock_movements disable trigger stock_movements_append_only;
            delete from stock_movements;
            alter table stock_movements enable trigger stock_movements_append_only;
            delete from stock_balances;
            delete from stock_operations;
            delete from expenses;
            delete from product_barcodes;
            delete from products;
            delete from shelves;
            delete from areas;
            delete from suppliers;
            delete from units;
            delete from asp_net_user_roles;
            delete from asp_net_users;
            """);
        AuditDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task EnsureDatabaseAsync(string adminConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString);
        if (!string.Equals(builder.Database, "postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Database creation must use the postgres maintenance database.");
        }

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        var current = await connection.ExecuteScalarAsync<string>("select current_database();");
        if (!string.Equals(current, "postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Database creation connected to an unexpected database.");
        }

        var exists = await connection.ExecuteScalarAsync<bool>(
            "select exists(select 1 from pg_database where datname = @Name);",
            new { Name = AuditDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {AuditDatabaseGuard.PermittedDatabase};");
    }

    private sealed class AuditApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public AuditApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            AuditDatabaseGuard.Require(builder.Database);
            _connectionString = builder.ConnectionString;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(Settings()));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings()));
        }

        private Dictionary<string, string?> Settings()
        {
            return new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Authentication:Issuer"] = Issuer,
                ["Authentication:Audience"] = Audience,
                ["Authentication:SigningKey"] = SigningKey,
                ["Authentication:AccessTokenMinutes"] = "15"
            };
        }
    }
}

internal static class AuditDatabaseTestsPassword
{
    public const string Value = "correct-horse";
}
