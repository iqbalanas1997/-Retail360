# Module Catalog

| Module | Purpose | Main concepts | Status |
|---|---|---|---|
| Catalog | Product master data | Product, Category, Brand, Unit, ProductBarcode, current prices, minimum-stock threshold | Backend implemented. Angular screens cover list, create, edit, barcodes, and category, brand, and unit names. |
| Inventory | On-hand stock and locations | Area, Shelf, stock balance per product and shelf, append-only movement | Backend implemented in `003_inventory.sql`. No stock column on Product. Angular screens cover stock, locations, opening stock, adjustments, and transfers. |
| Sales | POS and sales history | Sale, SaleLine, Payment | Planned |
| Purchasing | Supplier purchases | Purchase, PurchaseLine | Planned |
| Suppliers | Supplier records | Supplier | Backend implemented in `006_suppliers.sql`. Angular screens cover list, create, edit, activation, and soft delete. No payment, payable balance, or purchase history. |
| Customers | Customer records/receivables | Customer, CustomerPayment | Planned |
| Expenses | Shop expenses | Expense, ExpenseCategory | Planned |
| Reporting | Dashboard/reports | Sales/Purchase/Profit/Stock projections | Planned |
| Identity | Users/roles | User, Role | Backend sign-in is in place on the existing Identity tables. JWT access tokens and role checks are implemented. Permission is not a table. Angular sign-in calls `POST /api/auth/login` and keeps the access token in `sessionStorage` until expiry. |
| Audit | Important activity log | AuditEntry | Planned |

## Inventory scope
Inventory depends on Catalog for product identity, the product's unit, and `minimum_stock`. It does not own prices, barcodes, categories, or brands. Barcode stays a Catalog capability. There is no separate Barcode module.

A product that has a stock balance or a stock movement is not soft-deleted. The implemented Inventory scope is areas, shelves, opening stock, adjustments, and transfers. Damaged, expired, and missing goods are adjustment reasons. Shop-wide on-hand quantity is the sum of shelf balances, including inactive shelves. Low stock compares that sum with `minimum_stock`. An inactive shelf rejects inbound stock and allows outbound stock.

Inventory does not include Purchasing, Sales, suppliers, branches, batch or expiry dates, or a stock quantity on `products`. Movement rows are not edited or soft-deleted.

## Suppliers scope
A supplier is the shop's record of a business or person it can buy from. The record is the supplier id, name, optional contact details, and `is_active`. Names are unique among non-deleted rows, compared case-insensitively. Admin and Manager can change suppliers. Every shop role can read them. The Angular screens use those same routes. Supplier payment and Purchasing are separate modules and are not implemented.

## Duplication prevention
Before adding a new module or concept:
1. Search this catalog.
2. Search `DOMAIN_MODEL.md`.
3. Search the source code.
4. If an equivalent exists, extend it rather than creating a parallel concept.
