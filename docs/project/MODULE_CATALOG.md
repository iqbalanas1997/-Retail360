# Module Catalog

| Module | Purpose | Main concepts | Status |
|---|---|---|---|
| Catalog | Product master data | Product, Category, Brand, Unit, ProductBarcode, current prices, minimum-stock threshold | Backend implemented. Angular UI is not started. |
| Inventory | On-hand stock and locations | Area, Shelf, stock balance per product and shelf, append-only movement | Design approved in ADR-011. Not implemented. No stock column on Product. |
| Sales | POS and sales history | Sale, SaleLine, Payment | Planned |
| Purchasing | Supplier purchases | Purchase, PurchaseLine | Planned |
| Suppliers | Supplier records/payables | Supplier, SupplierPayment | Planned |
| Customers | Customer records/receivables | Customer, CustomerPayment | Planned |
| Expenses | Shop expenses | Expense, ExpenseCategory | Planned |
| Reporting | Dashboard/reports | Sales/Purchase/Profit/Stock projections | Planned |
| Identity | Users/roles | User, Role, Permission | In progress. Identity tables and the role seed remain. Dapper reads the roles. Domain keeps role names only. Sign-in and Permission are not modeled. |
| Audit | Important activity log | AuditEntry | Planned |

## Inventory scope
Inventory depends on Catalog for product identity, the product's unit, and `minimum_stock`. It does not own prices, barcodes, categories, or brands. Barcode stays a Catalog capability. There is no separate Barcode module.

The approved Inventory scope is areas, shelves, opening stock, adjustments, and transfers. Damaged, expired, and missing goods are adjustment reasons. Shop-wide on-hand quantity is the sum of shelf balances. Low stock compares that sum with `minimum_stock`.

Inventory does not include Purchasing, Sales, suppliers, branches, batch or expiry dates, or a stock quantity on `products`. Movement rows are not edited or soft-deleted.

## Duplication prevention
Before adding a new module or concept:
1. Search this catalog.
2. Search `DOMAIN_MODEL.md`.
3. Search the source code.
4. If an equivalent exists, extend it rather than creating a parallel concept.
