# Project Task Board

## Current phase
Phase 0 — Foundation and architecture. The Catalog, Inventory, Suppliers, and Purchasing backends are implemented. Angular sign-in, Catalog screens, Inventory screens, Supplier screens, and Purchase screens are in place. Sales are not started.

## Rules
- Only one primary task should be actively implemented at a time unless parallel work is explicitly planned.
- Each task must identify impacted modules.
- Mark tasks complete only after verification.
- Do not implement future-scope features merely because they appear in requirements.

## Backlog

### Foundation
- [x] Create .NET 8 solution and layer structure.
- [x] Create modern Angular application.
- [x] Configure PostgreSQL with Dapper and Npgsql. EF Core was removed; it is not the persistence stack.
- [x] Keep Domain free of framework packages. Shop role names stay in Domain.
- [x] Replace EF Core with Dapper and Npgsql. Keep the existing Identity tables.
- [x] Establish authentication/authorization baseline. JWT sign-in, database role checks, and protected Catalog and Inventory routes are in place. Permission tables, external identity, and multi-factor authentication are not.
- [ ] Establish shared error/validation/logging conventions.
- [ ] Add initial CI/build/test commands.

### Catalog
- [x] Product
- [x] Category
- [x] Brand
- [x] Unit
- [x] Barcode
- [x] Verify the Catalog backend: build, automated tests, and migration repeatability
- [x] Catalog screens for products, categories, brands, and units. Sales and Purchasing screens are not started.

### Inventory
- [x] Record the approved design: shelf balances, append-only movements, opening stock, adjustments, transfers, and shop-wide low stock.
- [x] Implement areas and shelves.
- [x] Implement stock balances and append-only movements.
- [x] Implement opening stock, adjustments, and transfers in one transaction per operation.
- [x] Enforce the non-negative shelf balance, including concurrent updates.
- [x] Identify low stock and out of stock from the sum of shelf balances.
- [x] Add a new SQL migration. Do not edit `001_initial_identity.sql` or `002_catalog.sql`.
- [x] Add domain tests, service tests, and PostgreSQL tests on an isolated database.
- [x] Add the reconciliation check that stored balances match movement history.
- [x] Inventory screens for stock, locations, opening stock, adjustments, and transfers. Sales and Purchasing screens are not started.

### Suppliers
- [x] Supplier record: name, optional contact details, activation, and soft delete.
- [ ] Supplier payments and payable balances.
- [x] Angular supplier screens.

### Sales / POS
- [ ] Product scan/search
- [ ] Cart
- [ ] Quantity
- [ ] Cash/credit payment
- [ ] Stock reduction
- [ ] Receipt
- [ ] Sales history

### Purchases
- [x] Purchase entry
- [x] Purchase lines
- [x] Receive purchased stock through the Inventory ledger
- [x] Angular purchase screens for the list, a recorded purchase, and receiving
- [ ] Stock increase
- [ ] Supplier history

### Business management
- [ ] Suppliers/payables
- [ ] Customers/receivables
- [ ] Expenses
- [ ] Dashboard/reports
- [ ] Users/roles
- [ ] Audit log

## Current task
Inventory and Catalog backend review fixes are in place: area deletion locks before it rechecks, shelf creation waits on that same area lock, stock posts require a client operation id, and a failed post leaves no operation row. `005_numeric_scale.sql` rejects extra fractional digits instead of rounding them. A product with stock history cannot be soft-deleted. Catalog tests refuse any database other than `retail360_catalog_test`. Sign-in is a JWT baseline with failed-attempt lockout. Catalog and Inventory routes require a shop role. Angular sign-in, Catalog screens, and Inventory screens are in place. An expired access token returns the open counter screen to sign-in. The stock list has no product-name query, so the stock screen filters names in the loaded page. Opening stock, adjustments, and transfers search catalog products by name. The Suppliers backend stores supplier records in `006_suppliers.sql`. Admin and Manager can change them. Every shop role can read them. Angular supplier screens cover the list, create, edit, activation, and soft delete. The Purchasing backend records a purchase and receives its goods through the Inventory ledger. Angular purchase screens list purchases, record or change a recorded purchase, and receive it onto shelves. The screen does not post stock itself. Payments and Sales are not started.
