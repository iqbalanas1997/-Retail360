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
using Retail360.Application.Catalog;
using Retail360.Application.Customers;
using Retail360.Application.Identity;
using Retail360.Application.Inventory;
using Retail360.Application.Sales;
using Retail360.Domain.Customers;
using Retail360.Domain.Identity;
using Retail360.Domain.Inventory;
using Retail360.Domain.Sales;
using Retail360.Infrastructure.Customers;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Inventory;
using Retail360.Infrastructure.Persistence.Sql;
using Retail360.Infrastructure.Sales;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("sales-database", DisableParallelization = true)]
public sealed class SaleDatabaseCollection : ICollectionFixture<SaleDatabaseFixture>
{
}

[Collection("sales-database")]
public sealed class SaleDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly SaleDatabaseFixture _database;

    public SaleDatabaseTests(SaleDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_shop_and_other_test_databases_without_connecting()
    {
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_identity_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_suppliers_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_purchasing_test"));
        SaleDatabaseGuard.Require(SaleDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Migration_is_recorded_once_on_the_sales_database()
    {
        await using var connection = await _database.OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations order by version;")).ToArray();
        var rows = await connection.ExecuteScalarAsync<long>(
            "select count(*) from schema_migrations where version = '008_sales';");

        Assert.Equal(SaleDatabaseGuard.PermittedDatabase, name);
        Assert.Contains("008_sales", versions);
        Assert.Contains("015_customer_credit", versions);
        Assert.Equal(1, rows);
        Assert.Equal(versions.Distinct(StringComparer.Ordinal).Count(), versions.Length);
    }

    [Fact]
    public async Task Complete_copies_the_catalog_price_and_a_replay_does_not_sell_again()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Tea", 0.10m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 5.000m);
        var operationId = Guid.NewGuid();
        var command = Command(operationId, product, shelf, 1.250m);

        var created = await sales.CompleteAsync(command, CancellationToken.None);
        var replay = await sales.CompleteAsync(command, CancellationToken.None);
        var changed = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(operationId, product, shelf, 1.000m),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId and shelf_id = @ShelfId;",
            new { ProductId = product, ShelfId = shelf });
        var movementType = await connection.ExecuteScalarAsync<string>(
            "select movement_type from stock_movements where product_id = @ProductId and movement_type = 'sale';",
            new { ProductId = product });

        Assert.Equal(0.13m, created.Total);
        Assert.Equal(0.13m, Assert.Single(created.Lines).LineTotal);
        Assert.Equal(0.10m, created.Lines[0].UnitPrice);
        Assert.Equal("Tea", created.Lines[0].ProductName);
        Assert.Equal(SaleText.Cash, created.PaymentMethod);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(created.ReceiptNumber, replay.ReceiptNumber);
        Assert.Equal(SaleRuleFailure.Conflict, changed.Failure);
        Assert.Equal("This operation key was already used for a different stock change.", changed.Message);
        Assert.Equal(3.750m, quantity);
        Assert.Equal("sale", movementType);
    }

    [Fact]
    public async Task Inactive_product_is_rejected_and_an_inactive_shelf_can_be_sold()
    {
        var sales = _database.CreateSales();
        var inactive = await SeedProductAsync("Hidden", 2.00m, false);
        var offered = await SeedProductAsync("Offered", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(offered, shelf, 2.000m);
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync("update shelves set is_active = false where id = @Id;", new { Id = shelf });
        }
        var removed = await SeedProductAsync("Gone", 1.00m, true);
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync(
                "update products set is_deleted = true where id = @Id;",
                new { Id = removed });
        }

        var hidden = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), inactive, shelf, 1m),
            CancellationToken.None));
        var deleted = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), removed, shelf, 1m),
            CancellationToken.None));
        var sold = await sales.CompleteAsync(Command(Guid.NewGuid(), offered, shelf, 1m), CancellationToken.None);

        Assert.Equal(SaleRuleFailure.Conflict, hidden.Failure);
        Assert.Equal("Product is inactive.", hidden.Message);
        Assert.Equal(SaleRuleFailure.NotFound, deleted.Failure);
        Assert.Equal(shelf, sold.Lines[0].ShelfId);
    }

    [Fact]
    public async Task Missing_product_and_insufficient_stock_leave_no_sale()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Rice", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1.000m);

        var missing = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), Guid.NewGuid(), shelf, 1m),
            CancellationToken.None));
        var shortStock = await Assert.ThrowsAsync<SaleStockException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 2m),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var salesForProduct = await connection.ExecuteScalarAsync<long>(
            "select count(*) from sale_lines where product_id = @ProductId;",
            new { ProductId = product });
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId;",
            new { ProductId = product });

        Assert.Equal(SaleRuleFailure.NotFound, missing.Failure);
        Assert.Equal(SaleRuleFailure.Conflict, shortStock.Failure);
        Assert.Equal("Shelf does not have enough stock.", shortStock.Message);
        var shortage = Assert.Single(shortStock.Shortages);
        Assert.Equal(product, shortage.ProductId);
        Assert.Equal("Rice", shortage.ProductName);
        Assert.Equal(shelf, shortage.ShelfId);
        Assert.Equal(2m, shortage.Required);
        Assert.Equal(1.000m, shortage.Available);
        Assert.Equal(0, salesForProduct);
        Assert.Equal(1.000m, quantity);
    }

    [Fact]
    public async Task Every_short_line_is_named_and_the_sale_is_not_stored()
    {
        var sales = _database.CreateSales();
        var rice = await SeedProductAsync("Basmati Rice", 2.00m, true);
        var milk = await SeedProductAsync("Milk", 1.50m, true);
        var shelfA = await SeedShelfAsync(true);
        var shelfB = await SeedShelfAsync(true);
        await OpenAsync(rice, shelfA, 6m);
        await OpenAsync(milk, shelfB, 2m);

        var error = await Assert.ThrowsAsync<SaleStockException>(() => sales.CompleteAsync(
            new CompleteSaleCommand(
                Guid.NewGuid(),
                [
                    new CompleteSaleLineCommand(rice, shelfA, 10m),
                    new CompleteSaleLineCommand(milk, shelfB, 4m)
                ],
                null,
                null),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var stored = await connection.ExecuteScalarAsync<long>(
            "select count(*) from sale_lines where product_id = any(@Ids);",
            new { Ids = new[] { rice, milk } });
        var riceQuantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @Id;",
            new { Id = rice });
        var milkQuantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @Id;",
            new { Id = milk });

        Assert.Equal(2, error.Shortages.Count);
        Assert.Contains(
            error.Shortages,
            item => item.ProductId == rice && item.ProductName == "Basmati Rice" && item.ShelfId == shelfA
                && item.Required == 10m && item.Available == 6m && item.ShelfName.Contains(" / ", StringComparison.Ordinal));
        Assert.Contains(
            error.Shortages,
            item => item.ProductId == milk && item.ProductName == "Milk" && item.ShelfId == shelfB
                && item.Required == 4m && item.Available == 2m);
        Assert.Equal(0, stored);
        Assert.Equal(6m, riceQuantity);
        Assert.Equal(2m, milkQuantity);
    }

    [Fact]
    public async Task A_later_catalog_edit_does_not_rewrite_the_receipt()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Old Tea", 3.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 2.000m);
        var created = await sales.CompleteAsync(Command(Guid.NewGuid(), product, shelf, 1m), CancellationToken.None);
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync(
                "update products set name = 'New Tea', selling_price = 9.00 where id = @Id;",
                new { Id = product });
        }

        var receipt = await sales.GetAsync(created.Id, CancellationToken.None);
        var listed = await sales.ListAsync(created.ReceiptNumber, 50, 0, CancellationToken.None);

        Assert.Equal("Old Tea", receipt.Lines[0].ProductName);
        Assert.Equal(3.00m, receipt.Lines[0].UnitPrice);
        Assert.Equal(created.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task A_customer_is_optional_and_does_not_change_the_cash_sale()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Customer Tea", 4.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 5.000m);
        var buyer = await SeedCustomerAsync("Amina", true, false, "03001234567");
        var inactive = await SeedCustomerAsync("Inactive buyer", false, false, "03007654321");
        var removed = await SeedCustomerAsync("Removed buyer", true, true, "03000000000");
        var other = await SeedCustomerAsync("Other buyer", true, false, "03001110000");

        var walkIn = await sales.CompleteAsync(Command(Guid.NewGuid(), product, shelf, 1m), CancellationToken.None);
        var operationId = Guid.NewGuid();
        var named = await sales.CompleteAsync(
            Command(operationId, product, shelf, 1m, buyer),
            CancellationToken.None);
        var replay = await sales.CompleteAsync(
            Command(operationId, product, shelf, 1m, buyer),
            CancellationToken.None);
        var inactiveSale = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, inactive),
            CancellationToken.None));
        var removedSale = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, removed),
            CancellationToken.None));
        var changedCustomer = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(operationId, product, shelf, 1m, other),
            CancellationToken.None));

        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync(
                "update customers set is_active = false, updated_at = now() where id = @Id;",
                new { Id = buyer });
        }

        var afterDeactivate = await sales.GetAsync(named.Id, CancellationToken.None);
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync(
                "update customers set is_deleted = true, updated_at = now() where id = @Id;",
                new { Id = buyer });
        }

        var afterDelete = await sales.GetAsync(named.Id, CancellationToken.None);
        var transactions = new PostgresTransaction(_database.Connections);
        var history = await new SaleStore(
            _database.Connections,
            transactions,
            new StockLedgerStore(_database.Connections, transactions),
            new CustomerCreditStore(_database.Connections, transactions)).ListForCustomerAsync(
            buyer,
            50,
            0,
            CancellationToken.None);
        await using var connectionAfter = await _database.OpenAsync();
        var quantity = await connectionAfter.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId;",
            new { ProductId = product });
        var audits = await connectionAfter.ExecuteScalarAsync<long>(
            """
            select count(*)
            from audit_entries
            where entity_id = @Id and action = 'sale.completed';
            """,
            new { Id = named.Id.ToString("D") });
        var details = await connectionAfter.ExecuteScalarAsync<string>(
            "select details::text from audit_entries where entity_id = @Id and action = 'sale.completed';",
            new { Id = named.Id.ToString("D") });
        var inactiveSales = await connectionAfter.ExecuteScalarAsync<long>(
            "select count(*) from sales where customer_id = @Id;",
            new { Id = inactive });

        Assert.Null(walkIn.CustomerId);
        Assert.Null(walkIn.CustomerName);
        Assert.Equal("cash", walkIn.PaymentMethod);
        Assert.Equal(4.00m, walkIn.Total);
        Assert.Equal(buyer, named.CustomerId);
        Assert.Equal("Amina", named.CustomerName);
        Assert.Equal(walkIn.Total, named.Total);
        Assert.Equal("cash", named.PaymentMethod);
        Assert.Equal(named.Id, replay.Id);
        Assert.Equal(SaleRuleFailure.Conflict, inactiveSale.Failure);
        Assert.Equal("Customer is inactive.", inactiveSale.Message);
        Assert.Equal(SaleRuleFailure.NotFound, removedSale.Failure);
        Assert.Equal(SaleRuleFailure.Conflict, changedCustomer.Failure);
        Assert.Equal(buyer, afterDeactivate.CustomerId);
        Assert.Equal("Amina", afterDeactivate.CustomerName);
        Assert.Equal("Amina", afterDelete.CustomerName);
        Assert.Equal(4.00m, Assert.Single(history).Total);
        Assert.Equal("cash", history[0].PaymentMethod);
        Assert.Equal(3.000m, quantity);
        Assert.Equal(1, audits);
        Assert.Contains(buyer.ToString("D"), details, StringComparison.Ordinal);
        Assert.DoesNotContain("private note", details, StringComparison.Ordinal);
        Assert.DoesNotContain("03001234567", details, StringComparison.Ordinal);
        Assert.Equal(0, inactiveSales);
    }

    [Fact]
    public async Task Concurrent_sales_of_the_last_unit_post_stock_once()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Last", 1.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1.000m);
        var firstCommand = Command(Guid.NewGuid(), product, shelf, 1m);
        var secondCommand = Command(Guid.NewGuid(), product, shelf, 1m);

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("select id from products where id = @Id for update;", new { Id = product }, gate);
        var first = sales.CompleteAsync(firstCommand, CancellationToken.None);
        var second = sales.CompleteAsync(secondCommand, CancellationToken.None);
        await WaitForWaitingSessionsAsync(2, first, second);
        await gate.CommitAsync();

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        await using var connection = await _database.OpenAsync();
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId;",
            new { ProductId = product });
        var saleMovements = await connection.ExecuteScalarAsync<long>(
            "select count(*) from stock_movements where product_id = @ProductId and movement_type = 'sale';",
            new { ProductId = product });

        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, results.Count(result => result is SaleStockException));
        Assert.Equal(0.000m, quantity);
        Assert.Equal(1, saleMovements);
    }

    [Fact]
    public async Task Credit_sale_charges_once_and_a_cash_sale_does_not()
    {
        var buyer = await SeedCustomerAsync("Credit Buyer", true, false, "0300111222");
        var product = await SeedProductAsync("Lentils", 4.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 5.000m);
        var operationId = Guid.NewGuid();
        var credit = _database.CreateSales().CompleteAsync(
            Command(operationId, product, shelf, 1.250m, buyer, SaleText.Credit),
            CancellationToken.None);

        var created = await credit;
        var replay = await _database.CreateSales().CompleteAsync(
            Command(operationId, product, shelf, 1.250m, buyer, SaleText.Credit),
            CancellationToken.None);
        var cash = await _database.CreateSales().CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer),
            CancellationToken.None);
        var changed = await Assert.ThrowsAsync<SaleRuleException>(() => _database.CreateSales().CompleteAsync(
            Command(operationId, product, shelf, 1.250m, buyer),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var charges = await connection.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id and entry_type = 'charge';",
            new { Id = buyer });
        var chargeSale = await connection.ExecuteScalarAsync<Guid>(
            "select sale_id from customer_ledger_entries where customer_id = @Id and entry_type = 'charge';",
            new { Id = buyer });
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId and shelf_id = @ShelfId;",
            new { ProductId = product, ShelfId = shelf });
        var details = await connection.ExecuteScalarAsync<string>(
            "select details::text from audit_entries where entity_id = @Id and action = 'sale.completed';",
            new { Id = created.Id.ToString("D") });
        var audits = await connection.ExecuteScalarAsync<long>(
            "select count(*) from audit_entries where entity_id = @Id and action = 'sale.completed';",
            new { Id = created.Id.ToString("D") });

        Assert.Equal(SaleText.Credit, created.PaymentMethod);
        Assert.Equal(5.00m, created.Total);
        Assert.Equal(buyer, created.CustomerId);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(SaleText.Cash, cash.PaymentMethod);
        Assert.Equal(SaleRuleFailure.Conflict, changed.Failure);
        Assert.Equal(1, charges);
        Assert.Equal(created.Id, chargeSale);
        Assert.Equal(2.750m, quantity);
        Assert.Equal(1, audits);
        Assert.Contains("\"paymentMethod\": \"credit\"", details, StringComparison.Ordinal);
        Assert.DoesNotContain("0300111222", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credit_without_a_customer_or_a_positive_total_is_rejected()
    {
        var sales = _database.CreateSales();
        var free = await SeedProductAsync("Sample", 0m, true);
        var priced = await SeedProductAsync("Rice", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(free, shelf, 2m);
        await OpenAsync(priced, shelf, 2m);
        var buyer = await SeedCustomerAsync("No Credit", true, false, "0300222333");

        var missingCustomer = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), priced, shelf, 1m, null, SaleText.Credit),
            CancellationToken.None));
        var zero = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), free, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var charges = await connection.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id;",
            new { Id = buyer });
        var freeStock = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @Id;",
            new { Id = free });

        Assert.Equal(SaleRuleFailure.Invalid, missingCustomer.Failure);
        Assert.Equal("A credit sale needs a customer.", missingCustomer.Message);
        Assert.Equal(SaleRuleFailure.Invalid, zero.Failure);
        Assert.Equal("A credit sale total must be greater than zero.", zero.Message);
        Assert.Equal(0, charges);
        Assert.Equal(2m, freeStock);
    }

    [Fact]
    public async Task Failed_credit_sale_does_not_leave_a_charge()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Short", 3.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1m);
        var buyer = await SeedCustomerAsync("Short Buyer", true, false, "0300333444");

        var error = await Assert.ThrowsAsync<SaleStockException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 2m, buyer, SaleText.Credit),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var charges = await connection.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id;",
            new { Id = buyer });
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @Id;",
            new { Id = product });

        Assert.Equal(SaleRuleFailure.Conflict, error.Failure);
        Assert.Equal("Shelf does not have enough stock.", error.Message);
        Assert.Equal(0, charges);
        Assert.Equal(1m, quantity);
    }

    [Fact]
    public async Task Payments_allocate_to_the_oldest_charge_and_reject_a_reused_operation()
    {
        var buyer = await SeedCustomerAsync("Oldest", true, false, "0300444555");
        var other = await SeedCustomerAsync("Other payer", true, false, "0300555666");
        var product = await SeedProductAsync("Beans", 10.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 3m);
        var early = await _database.CreateSales(DateTimeOffset.Parse("2026-10-20T00:00:00Z")).CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None);
        var later = await _database.CreateSales(DateTimeOffset.Parse("2026-10-21T00:00:00Z")).CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None);
        var credit = _database.CreateCredit(DateTimeOffset.Parse("2026-10-22T00:00:00Z"));
        var operationId = Guid.NewGuid();

        var payment = await credit.RecordPaymentAsync(
            buyer,
            new RecordCustomerPaymentCommand(operationId, 15.00m),
            CancellationToken.None);
        var replay = await credit.RecordPaymentAsync(
            buyer,
            new RecordCustomerPaymentCommand(operationId, 15.00m),
            CancellationToken.None);
        var differentAmount = await Assert.ThrowsAsync<CustomerRuleException>(() => credit.RecordPaymentAsync(
            buyer,
            new RecordCustomerPaymentCommand(operationId, 10.00m),
            CancellationToken.None));
        var differentCustomer = await Assert.ThrowsAsync<CustomerRuleException>(() => credit.RecordPaymentAsync(
            other,
            new RecordCustomerPaymentCommand(operationId, 15.00m),
            CancellationToken.None));
        var overpay = await Assert.ThrowsAsync<CustomerRuleException>(() => credit.RecordPaymentAsync(
            buyer,
            new RecordCustomerPaymentCommand(Guid.NewGuid(), 6.00m),
            CancellationToken.None));
        var balance = await credit.GetBalanceAsync(buyer, CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var payments = await connection.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id and entry_type = 'payment';",
            new { Id = buyer });
        var audits = await connection.ExecuteScalarAsync<long>(
            """
            select count(*)
            from audit_entries
            where entity_id = @Id and action = 'customer.payment_recorded';
            """,
            new { Id = buyer.ToString("D") });
        var details = await connection.ExecuteScalarAsync<string>(
            """
            select details::text
            from audit_entries
            where entity_id = @Id and action = 'customer.payment_recorded';
            """,
            new { Id = buyer.ToString("D") });

        Assert.Equal(15.00m, payment.Amount);
        Assert.Equal(5.00m, payment.Outstanding);
        Assert.Equal(early.Id, payment.Allocations[0].SaleId);
        Assert.Equal(10.00m, payment.Allocations[0].Amount);
        Assert.Equal(later.Id, payment.Allocations[1].SaleId);
        Assert.Equal(5.00m, payment.Allocations[1].Amount);
        Assert.Equal(payment.Id, replay.Id);
        Assert.Equal(CustomerRuleFailure.Conflict, differentAmount.Failure);
        Assert.Equal("This operation key was already used for a different payment.", differentAmount.Message);
        Assert.Equal(CustomerRuleFailure.Conflict, differentCustomer.Failure);
        Assert.Equal(CustomerRuleFailure.Conflict, overpay.Failure);
        Assert.Equal("Payment is larger than the outstanding balance.", overpay.Message);
        Assert.Equal(5.00m, balance.Outstanding);
        Assert.Equal(1, payments);
        Assert.Equal(1, audits);
        Assert.Contains("15.00", details, StringComparison.Ordinal);
        Assert.DoesNotContain("0300444555", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ledger_rejects_a_second_charge_an_unbalanced_allocation_and_any_change()
    {
        var buyer = await SeedCustomerAsync("Guard", true, false, "0300666777");
        var product = await SeedProductAsync("Oil", 8.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1m);
        var sale = await _database.CreateSales().CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var chargeId = await connection.ExecuteScalarAsync<Guid>(
            "select id from customer_ledger_entries where sale_id = @SaleId;",
            new { SaleId = sale.Id });

        var secondCharge = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into customer_ledger_entries (
                id, operation_id, operation_fingerprint, customer_id, sale_id,
                entry_type, amount, occurred_at, created_at)
            values (
                @Id, @OperationId, 'charge-again', @CustomerId, @SaleId,
                'charge', 8.00, now(), now());
            """,
            new
            {
                Id = Guid.NewGuid(),
                OperationId = Guid.NewGuid(),
                CustomerId = buyer,
                SaleId = sale.Id
            }));
        var changed = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "update customer_ledger_entries set amount = 7.00 where id = @Id;",
            new { Id = chargeId }));
        var removed = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "delete from customer_ledger_entries where id = @Id;",
            new { Id = chargeId }));
        await using var over = await connection.BeginTransactionAsync();
        var paymentId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            insert into customer_ledger_entries (
                id, operation_id, operation_fingerprint, customer_id, sale_id,
                entry_type, amount, occurred_at, created_at)
            values (
                @Id, @OperationId, 'too-much', @CustomerId, null,
                'payment', 8.00, now(), now());
            insert into customer_payment_allocations (
                id, payment_entry_id, charge_entry_id, amount, created_at)
            values (@AllocationId, @Id, @ChargeId, 9.00, now());
            """,
            new
            {
                Id = paymentId,
                OperationId = Guid.NewGuid(),
                CustomerId = buyer,
                AllocationId = Guid.NewGuid(),
                ChargeId = chargeId
            },
            over);
        PostgresException unbalanced;
        try
        {
            await over.CommitAsync();
            throw new InvalidOperationException("An allocation above the charge committed.");
        }
        catch (PostgresException exception)
        {
            unbalanced = exception;
        }

        await using var connectionAfter = await _database.OpenAsync();
        var rows = await connectionAfter.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id;",
            new { Id = buyer });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, secondCharge.SqlState);
        Assert.Equal("uq_customer_ledger_entries_charge_sale", secondCharge.ConstraintName);
        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, changed.SqlState);
        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, removed.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, unbalanced.SqlState);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Outstanding_customer_cannot_be_removed_and_a_settled_customer_can()
    {
        var buyer = await SeedCustomerAsync("Settle", true, false, "0300777888");
        var product = await SeedProductAsync("Soap", 6.50m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1m);
        var sale = await _database.CreateSales().CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None);
        var customers = _database.CreateCustomers(DateTimeOffset.Parse("2026-10-22T00:00:00Z"));
        var blocked = await Assert.ThrowsAsync<CustomerRuleException>(() =>
            customers.DeleteAsync(buyer, CancellationToken.None));
        await _database.CreateCredit(DateTimeOffset.Parse("2026-10-22T01:00:00Z")).RecordPaymentAsync(
            buyer,
            new RecordCustomerPaymentCommand(Guid.NewGuid(), 6.50m),
            CancellationToken.None);

        await customers.DeleteAsync(buyer, CancellationToken.None);
        var receipt = await _database.CreateSales().GetAsync(sale.Id, CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var deleted = await connection.ExecuteScalarAsync<bool>(
            "select is_deleted from customers where id = @Id;",
            new { Id = buyer });
        var charges = await connection.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id;",
            new { Id = buyer });

        Assert.Equal(CustomerRuleFailure.Conflict, blocked.Failure);
        Assert.Equal("Customer still has an outstanding balance.", blocked.Message);
        Assert.True(deleted);
        Assert.Equal(SaleText.Credit, receipt.PaymentMethod);
        Assert.Equal("Settle", receipt.CustomerName);
        Assert.Equal(2, charges);
    }

    [Fact]
    public async Task Concurrent_credit_sale_and_delete_do_not_leave_a_charge_on_a_removed_customer()
    {
        var buyer = await SeedCustomerAsync("Race", true, false, "0300888999");
        var product = await SeedProductAsync("Race goods", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1m);
        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from customers where id = @Id for update;",
            new { Id = buyer },
            gate);
        var sale = _database.CreateSales().CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None);
        var delete = _database.CreateCustomers(DateTimeOffset.Parse("2026-10-22T00:00:00Z"))
            .DeleteAsync(buyer, CancellationToken.None);
        await WaitForWaitingSessionsAsync(2, sale, delete);
        await gate.CommitAsync();
        var saleError = await WrapAsync(sale);
        var deleteError = await WrapCustomerAsync(delete);
        await using var connection = await _database.OpenAsync();
        var removed = await connection.ExecuteScalarAsync<bool>(
            "select is_deleted from customers where id = @Id;",
            new { Id = buyer });
        var charges = await connection.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id and entry_type = 'charge';",
            new { Id = buyer });

        Assert.Equal(1, (saleError is null ? 1 : 0) + (deleteError is null ? 1 : 0));
        Assert.False(removed && charges > 0);
        if (saleError is null)
        {
            Assert.False(removed);
            Assert.Equal(1, charges);
        }
        else
        {
            Assert.True(removed);
            Assert.Equal(0, charges);
        }
    }

    [Fact]
    public async Task Concurrent_payments_cannot_settle_more_than_the_charge()
    {
        var buyer = await SeedCustomerAsync("Double pay", true, false, "0300999000");
        var product = await SeedProductAsync("Double", 4.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1m);
        await _database.CreateSales().CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None);
        var credit = _database.CreateCredit(DateTimeOffset.Parse("2026-10-22T00:00:00Z"));
        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from customers where id = @Id for update;",
            new { Id = buyer },
            gate);
        var first = credit.RecordPaymentAsync(buyer, new RecordCustomerPaymentCommand(Guid.NewGuid(), 4.00m), CancellationToken.None);
        var second = credit.RecordPaymentAsync(buyer, new RecordCustomerPaymentCommand(Guid.NewGuid(), 4.00m), CancellationToken.None);
        await WaitForWaitingSessionsAsync(2, first, second);
        await gate.CommitAsync();
        var results = await Task.WhenAll(WrapCustomerAsync(first), WrapCustomerAsync(second));
        await using var connection = await _database.OpenAsync();
        var payments = await connection.ExecuteScalarAsync<long>(
            "select count(*) from customer_ledger_entries where customer_id = @Id and entry_type = 'payment';",
            new { Id = buyer });
        var outstanding = await connection.ExecuteScalarAsync<decimal>(
            """
            select coalesce(sum(case when entry_type = 'charge' then amount else -amount end), 0)
            from customer_ledger_entries
            where customer_id = @Id;
            """,
            new { Id = buyer });

        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, results.Count(result => result is CustomerRuleException));
        Assert.Equal(1, payments);
        Assert.Equal(0m, outstanding);
    }

    [Fact]
    public async Task Cashier_can_record_a_payment_and_inventory_staff_cannot()
    {
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        var buyer = await SeedCustomerAsync("Counter pay", true, false, "0300123000");
        var product = await SeedProductAsync("Counter goods", 3.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1m);
        await _database.CreateSales().CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 1m, buyer, SaleText.Credit),
            CancellationToken.None);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var body = new { operationId = Guid.NewGuid(), amount = 1.25m };

        var staffPayment = await SendAsync(client, $"/api/customers/{buyer}/payments", staffToken, HttpMethod.Post, body);
        var cashierPayment = await SendAsync(client, $"/api/customers/{buyer}/payments", cashierToken, HttpMethod.Post, body);
        var cashierBalance = await SendAsync(client, $"/api/customers/{buyer}/balance", cashierToken);
        var staffBalance = await SendAsync(client, $"/api/customers/{buyer}/balance", staffToken);

        Assert.Equal(HttpStatusCode.Forbidden, staffPayment.StatusCode);
        Assert.Equal(HttpStatusCode.Created, cashierPayment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cashierBalance.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffBalance.StatusCode);
        using var paid = JsonDocument.Parse(await cashierPayment.Content.ReadAsStringAsync());
        Assert.Equal(1.75m, paid.RootElement.GetProperty("outstanding").GetDecimal());
        Assert.Equal(1.25m, paid.RootElement.GetProperty("allocations")[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected_and_inventory_staff_cannot_sell()
    {
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var product = await SeedProductAsync("Counter", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 3.000m);
        var body = new
        {
            operationId = Guid.NewGuid(),
            lines = new[] { new { productId = product, shelfId = shelf, quantity = 1.000m } }
        };

        var anonymous = await client.GetAsync("/api/sales");
        var cashierRead = await SendAsync(client, "/api/sales", cashierToken);
        var staffSale = await SendAsync(client, "/api/sales", staffToken, HttpMethod.Post, body);
        var cashierSale = await SendAsync(client, "/api/sales", cashierToken, HttpMethod.Post, body);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cashierRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffSale.StatusCode);
        Assert.Equal(HttpStatusCode.Created, cashierSale.StatusCode);
    }

    private async Task<Guid> SeedProductAsync(string name, decimal sellingPrice, bool active)
    {
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
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
                1.00, @SellingPrice, 0.000, @Active,
                now(), now(), false);
            """,
            new
            {
                Id = unitId,
                Name = "Unit " + unitId.ToString("N"),
                ProductId = productId,
                ProductName = name,
                SellingPrice = sellingPrice,
                Active = active
            });
        return productId;
    }

    private async Task<Guid> SeedShelfAsync(bool active)
    {
        var areaId = Guid.NewGuid();
        var shelfId = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into areas (id, name, is_active, created_at, updated_at, is_deleted)
            values (@AreaId, @AreaName, true, now(), now(), false);
            insert into shelves (id, area_id, name, is_active, created_at, updated_at, is_deleted)
            values (@ShelfId, @AreaId, @ShelfName, @Active, now(), now(), false);
            """,
            new
            {
                AreaId = areaId,
                AreaName = "Area " + areaId.ToString("N"),
                ShelfId = shelfId,
                ShelfName = "Shelf " + shelfId.ToString("N"),
                Active = active
            });
        return shelfId;
    }

    private async Task OpenAsync(Guid productId, Guid shelfId, decimal quantity)
    {
        var transactions = new PostgresTransaction(_database.Connections);
        var ledger = new StockLedgerStore(_database.Connections, transactions);
        await ledger.PostAsync(
            new StockPosting(
                Guid.NewGuid(),
                "opening|" + Guid.NewGuid().ToString("N"),
                productId,
                [new StockLine(shelfId, quantity, StockDirection.In, StockMovementType.Opening, "Opening count", null)],
                DateTimeOffset.Parse("2026-10-20T00:00:00Z")),
            CancellationToken.None);
    }

    private static CompleteSaleCommand Command(
        Guid operationId,
        Guid productId,
        Guid shelfId,
        decimal quantity,
        Guid? customerId = null,
        string? paymentMethod = null)
    {
        return new CompleteSaleCommand(
            operationId,
            [new CompleteSaleLineCommand(productId, shelfId, quantity)],
            customerId,
            paymentMethod);
    }

    private async Task<Guid> SeedCustomerAsync(string name, bool active, bool deleted, string phone)
    {
        var id = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into customers (
                id, name, phone, email, address, notes, is_active,
                created_at, updated_at, is_deleted)
            values (
                @Id, @Name, @Phone, null, null, 'private note', @Active,
                now(), now(), @Deleted);
            """,
            new { Id = id, Name = name, Phone = phone, Active = active, Deleted = deleted });
        return id;
    }

    private async Task<string> CreateUserAsync(string role)
    {
        var userName = role.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()
            + "-"
            + Guid.NewGuid().ToString("N");
        await _database.Directory.CreateAsync(
            new NewShopUser(Guid.NewGuid().ToString("D"), userName, _database.Passwords.Hash(Password), role),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        return userName;
    }

    private static async Task<string> LoginAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login did not return an access token.");
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        string token,
        HttpMethod? method = null,
        object? body = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }

    private async Task WaitForWaitingSessionsAsync(int sessionCount, params Task[] workers)
    {
        await using var watcher = await _database.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long waiting = 0;
        while (DateTime.UtcNow < deadline)
        {
            var faulted = workers.FirstOrDefault(worker => worker.IsFaulted);
            if (faulted is not null)
            {
                await faulted;
            }

            waiting = await watcher.ExecuteScalarAsync<long>(
                """
                select count(*)
                from pg_stat_activity
                where datname = current_database()
                  and pid <> pg_backend_pid()
                  and wait_event_type = 'Lock';
                """);
            if (waiting >= sessionCount)
            {
                return;
            }

            await Task.Delay(50);
        }

        var activity = (await watcher.QueryAsync<LockWaitRow>(
            """
            select a.pid, a.state, a.wait_event_type, a.wait_event, left(coalesce(a.query, ''), 160) as query
            from pg_stat_activity a
            where a.datname = current_database()
              and a.pid <> pg_backend_pid();
            """)).ToArray();
        var detail = string.Join(
            " | ",
            activity.Select(row => $"{row.Pid} {row.State} {row.WaitEventType}/{row.WaitEvent} {row.Query}"));
        var states = string.Join(", ", workers.Select(worker => worker.Status.ToString()));
        throw new TimeoutException(
            $"Expected {sessionCount} waiting sessions but found {waiting}. Task states: {states}. {detail}");
    }

    private sealed class LockWaitRow
    {
        public int Pid { get; init; }

        public string State { get; init; } = string.Empty;

        public string? WaitEventType { get; init; }

        public string? WaitEvent { get; init; }

        public string Query { get; init; } = string.Empty;
    }

    private static async Task<Exception?> WrapAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception) when (exception is InventoryRuleException or SaleRuleException)
        {
            return exception;
        }
    }

    private static async Task<Exception?> WrapCustomerAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception) when (exception is CustomerRuleException or SaleRuleException)
        {
            return exception;
        }
    }
}

public sealed class SaleDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-sales-test-signing-key";

    public const string Issuer = "retail360-sales-tests";

    public const string Audience = "retail360-sales-tests";

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
            Database = SaleDatabaseGuard.PermittedDatabase
        };
        SaleDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            SaleDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        var runner = new SqlMigrationRunner(Connections);
        await runner.ApplyAsync(CancellationToken.None);
        await runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new SaleApiFactory(builder.ConnectionString);
    }

    public SaleService CreateSales()
    {
        return CreateSales(DateTimeOffset.Parse("2026-10-20T00:00:00Z"));
    }

    public SaleService CreateSales(DateTimeOffset utcNow)
    {
        var transactions = new PostgresTransaction(Connections);
        return new SaleService(
            new SaleStore(
                Connections,
                transactions,
                new StockLedgerStore(Connections, transactions),
                new CustomerCreditStore(Connections, transactions)),
            new FixedClock(utcNow));
    }

    public CustomerService CreateCustomers(DateTimeOffset utcNow)
    {
        var transactions = new PostgresTransaction(Connections);
        var credit = new CustomerCreditStore(Connections, transactions);
        return new CustomerService(
            new CustomerStore(Connections, transactions),
            new SaleStore(Connections, transactions, new StockLedgerStore(Connections, transactions), credit),
            credit,
            new FixedClock(utcNow));
    }

    public CustomerCreditService CreateCredit(DateTimeOffset utcNow)
    {
        var transactions = new PostgresTransaction(Connections);
        return new CustomerCreditService(
            new CustomerStore(Connections, transactions),
            new CustomerCreditStore(Connections, transactions),
            new FixedClock(utcNow));
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        SaleDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
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
            new { Name = SaleDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {SaleDatabaseGuard.PermittedDatabase};");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class SaleApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public SaleApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            SaleDatabaseGuard.Require(builder.Database);
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
