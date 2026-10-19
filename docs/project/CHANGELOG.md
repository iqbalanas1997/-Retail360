# Project Knowledge Changelog

Use this file for meaningful project-level changes that affect future AI context.

## Format
### YYYY-MM-DD — Title
- What changed
- Why
- Impacted modules
- Documentation updated

## Entries
### 2026-10-20 — POS screens
- Added the counter screen at `/pos`, the sales list, and the printable receipt. Admin, Manager, and Cashier can complete a cash sale. Every shop role can read sales. Inventory Staff cannot complete one. The screen sends the operation id, product, shelf, and quantity. The server calculates the price and totals.
- Impacted modules: Sales.
- Documentation updated: `README.md`, `AGENTS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `POS_UX.md`, `CHANGELOG.md`.

### 2026-10-20 — Cash sales
- Added completed cash sales. The server copies the catalog selling price and product name, calculates totals, and posts outbound `sale` movements through the Inventory ledger in the same transaction. Admin, Manager, and Cashier can sell. Every shop role can read a sale. Inventory Staff cannot sell. There is no Angular POS screen.
- Impacted modules: Sales, Inventory.
- Documentation updated: `README.md`, `AGENTS.md`, `ARCHITECTURE.md`, `DECISIONS.md`, `DATABASE.md`, `DOMAIN_MODEL.md`, `API_CONTRACTS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-20 — Purchase screens
- Added Angular screens for the purchase list, recording a purchase, and receiving it. Every shop role can read purchases. Admin and Manager can record or change a recorded purchase. Admin, Manager, and Inventory Staff can receive one. The list filters by supplier and status. The screen shows a working total and saves the server total. Receiving sends a client operation id and a shelf for every line. The screen does not post stock itself.
- Impacted modules: Purchasing.
- Documentation updated: `README.md`, `ARCHITECTURE.md`, `MODULE_CATALOG.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-20 — Purchase records and receiving
- Added purchase documents and lines. Recording a purchase does not change stock. Receiving posts inbound `purchase` movements through the existing Inventory ledger in one transaction. The server calculates totals. A supplier referenced by a purchase cannot be soft-deleted.
- Impacted modules: Purchasing, Inventory, Suppliers.
- Documentation updated: `README.md`, `AGENTS.md`, `ARCHITECTURE.md`, `DECISIONS.md`, `DATABASE.md`, `DOMAIN_MODEL.md`, `API_CONTRACTS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-19 — Supplier screens
- Added Angular screens for the supplier list, supplier details, activation, and soft delete. Name search and paging use the supplier API. Admin and Manager can change a supplier. Cashier and Inventory Staff can read one. Payments and purchases are not included.
- Impacted modules: Suppliers.
- Documentation updated: `README.md`, `ARCHITECTURE.md`, `MODULE_CATALOG.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-19 — Supplier records
- Added supplier master data: name, optional contact details, activation, and soft delete. Names are unique among non-deleted rows, case-insensitively. Admin and Manager can change suppliers. Every shop role can read them. Payments, purchases, and supplier screens are not included.
- Impacted modules: Suppliers, Identity.
- Documentation updated: `README.md`, `AGENTS.md`, `ARCHITECTURE.md`, `DECISIONS.md`, `DATABASE.md`, `DOMAIN_MODEL.md`, `API_CONTRACTS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-14 — Frontend hardening
- An open counter screen returns to sign-in when the access token expires. A second click does not send another sign-in, catalog save, or stock post while the first request is still running. A late stock or product list response does not replace a newer one. Opening stock, adjustments, and transfers can search catalog products by name.
- Impacted modules: Identity, Catalog, Inventory.
- Documentation updated: `README.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-14 — Inventory screens
- Added Angular screens for stock, locations, opening stock, adjustments, and transfers. Stock posts keep one operation id for a retry of the same submission. Cashier can read stock and locations. Admin, Manager, and Inventory Staff can change them. The stock list has no product-name query, so the screen filters names in the loaded page.
- Impacted modules: Inventory.
- Documentation updated: `README.md`, `ARCHITECTURE.md`, `DECISIONS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-14 — Catalog screens
- Added Angular sign-in and Catalog screens for products, categories, brands, and units. The dev server proxies `/api` because the API does not send CORS headers. The access token stays in `sessionStorage` until expiry. Inventory screens are not included.
- Impacted modules: Catalog, Identity.
- Documentation updated: `README.md`, `ARCHITECTURE.md`, `MODULE_CATALOG.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-14 — Product delete keeps stock history
- A product with a stock balance row or a stock movement cannot be soft-deleted, including when the quantity is zero. The check runs after the product row lock. A product with no Inventory history can still be soft-deleted.
- Impacted modules: Catalog, Inventory.
- Documentation updated: `API_CONTRACTS.md`, `DATABASE.md`, `DECISIONS.md`, `DOMAIN_MODEL.md`, `MODULE_CATALOG.md`, `TASKS.md`, `README.md`, `AGENTS.md`, `CHANGELOG.md`.

### 2026-10-14 — Numeric scale checks
- Prices and quantities are stored as unconstrained `numeric`. Checks reject a value that is not already rounded to 2 fractional digits for money or 3 for quantity, and reject a value above the old `numeric(12,2)` or `numeric(14,3)` maximum.
- A shelf total above `99999999999.999` is a conflict, and that stock operation is rolled back.
- Impacted modules: Catalog, Inventory.
- Documentation updated: `DATABASE.md`, `DECISIONS.md`, `DOMAIN_MODEL.md`, `API_CONTRACTS.md`, `ARCHITECTURE.md`, `README.md`, `AGENTS.md`, `TASKS.md`, `CHANGELOG.md`.

### 2026-10-13 — Sign-in lockout
- A wrong password for an existing unlocked user increments `access_failed_count` in one update. The fifth failure sets `lockout_end` for five minutes and resets the count. A successful login clears the count only when that update still sees the account as unlocked.
- Admin bootstrap locks the Admin role row and rechecks before insert, so two startups create one Admin.
- Impacted modules: Identity.
- Documentation updated: `API_CONTRACTS.md`, `DECISIONS.md`, `README.md`, `AGENTS.md`, `CHANGELOG.md`.

### 2026-10-13 — Authentication baseline
- Added `POST /api/auth/login` on the existing Identity tables. Passwords are hashed. Access tokens are short-lived JWTs. Each request loads roles from the database.
- Catalog and Inventory routes now require a signed-in shop role. Cashier can read. Admin and Manager can change the catalog. Admin, Manager, and Inventory Staff can change locations and stock.
- No migration was added. The signing key and the optional first-admin password stay outside source control. The tracked database password placeholder is empty.
- Impacted modules: Identity, Catalog, Inventory, API.
- Documentation updated: `API_CONTRACTS.md`, `ARCHITECTURE.md`, `DECISIONS.md`, `DATABASE.md`, `DOMAIN_MODEL.md`, `MODULE_CATALOG.md`, `TASKS.md`, `README.md`, `AGENTS.md`, `CHANGELOG.md`.

### 2026-10-13 — Remaining review coverage
- Shelf creation now has a lock-wait test: it blocks on the area row, then receives not-found after that area is soft-deleted, and no shelf row remains.
- A rejected stock check now asserts that the failed `operationId` is absent from `stock_operations` and `stock_movements`.
- `README.md` and `ARCHITECTURE.md` now describe `004_stock_operations.sql`, the operation-id retry rule, and that authentication is not implemented.
- Impacted modules: Inventory.
- Documentation updated: `README.md`, `ARCHITECTURE.md`, `CHANGELOG.md`.

### 2026-10-12 — Backend review remediation
- Area soft-delete locks the area and its shelves, then rechecks shelves and stock history. Shelf creation locks the area. Balance reads keep stock visible if an area row is soft-deleted.
- Stock opening, adjustment, and transfer require a client `operationId`. `004_stock_operations.sql` stores that key and its request fingerprint. A repeat returns the original movements. A different payload for the same key is a conflict. `001`, `002`, and `003` were not edited.
- Missing area, unit, category, and brand references return not-found. Only the balance check constraint is reported as insufficient stock.
- Catalog integration tests refuse `retail360` and `retail360_inventory_test` before they migrate or write.
- Impacted modules: Inventory, Catalog, API.
- Documentation updated: `API_CONTRACTS.md`, `DATABASE.md`, `DECISIONS.md`, `DOMAIN_MODEL.md`, `TASKS.md`, `CHANGELOG.md`, `AGENTS.md`.

### 2026-10-12 — Inventory lock coordination
- Shelf soft-delete now locks the shelf row and rechecks stock history in that same transaction.
- Stock posting locks the product row and rejects a product that is already soft-deleted. Catalog product update and soft-delete lock that same row before changing it.
- Regression tests either hold the conflicting row lock until the other session is waiting, or hold both stock transactions open until both have started, then release them.
- Impacted modules: Inventory, Catalog.
- Documentation updated: `DECISIONS.md` (ADR-011), `ARCHITECTURE.md`, `DOMAIN_MODEL.md`, `DATABASE.md`, `CHANGELOG.md`.

### 2026-10-11 — Inventory backend
- Added areas, shelves, shelf balances, and the append-only movement ledger in `003_inventory.sql`.
- Opening stock, adjustments, and transfers update the stored balance in the same transaction. An inactive shelf rejects inbound stock and allows outbound stock. Concurrent outbound requests cannot take a shelf below zero.
- Integration tests use `retail360_inventory_test` and refuse any other database name before cleanup.
- `dotnet build Retail360.sln`: 0 warnings, 0 errors. `dotnet test Retail360.sln`: 48 passed, 0 failed, 0 skipped.
- The shop database `retail360` recorded `003_inventory` through the existing migration runner. The script only created tables, indexes, checks, and the append-only trigger. `001_initial_identity.sql` and `002_catalog.sql` were not edited.
- Impacted modules: Inventory.
- Documentation updated: `DECISIONS.md` (ADR-011), `ARCHITECTURE.md`, `DOMAIN_MODEL.md`, `DATABASE.md`, `API_CONTRACTS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `AGENTS.md`, `README.md`, `.cursor/rules/40-database.mdc`, `CHANGELOG.md`.

### 2026-10-10 — Inventory design approved
- Recorded the approved Inventory design: one shop, areas and shelves, stock per product and shelf, an append-only movement ledger, and a stored balance updated in the same transaction.
- Opening stock, adjustments, and transfers are in scope. Damaged, expired, and missing are adjustment reasons. Purchasing and Sales integration are not.
- Shelf quantity uses `numeric(14,3)` and cannot go below zero. Movements are not edited or soft-deleted. Low stock compares the sum of shelf balances with Catalog `minimum_stock`.
- No Inventory code, migration, test, or database table was added.
- Impacted modules: Inventory, Catalog.
- Documentation updated: `DECISIONS.md` (ADR-011), `ARCHITECTURE.md`, `DOMAIN_MODEL.md`, `DATABASE.md`, `API_CONTRACTS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `REQUIREMENTS.md`, `AGENTS.md`, `.cursor/rules/40-database.mdc`, `CHANGELOG.md`.

### 2026-10-10 — Catalog backend verification
- Reconciled Catalog documentation so price scale, minimum stock, optional category and brand, and barcode ownership match the approved model.
- Added tests for validation, duplicate barcodes, soft deletion, inactive barcode lookup, and PostgreSQL constraints.
- `dotnet build Retail360.sln`: 0 warnings, 0 errors. `dotnet test`: 22 passed, 0 failed.
- The shop database `retail360` still has one `schema_migrations` row each for `001_initial_identity` and `002_catalog` after two API starts. Admin, Manager, Cashier, and Inventory Staff remain.
- Impacted modules: Catalog.
- Documentation updated: `AGENTS.md`, `.cursor/rules/10-architecture.mdc`, `.cursor/rules/40-database.mdc`, `REQUIREMENTS.md`, `ARCHITECTURE.md`, `DATABASE.md`, `DOMAIN_MODEL.md`, `CHANGELOG.md`, `TASKS.md`.

### 2026-10-10 — Catalog backend
- Added category, brand, unit, product, and product barcode persistence with `002_catalog.sql`.
- Prices are `numeric(12,2)`, minimum stock is `numeric(14,3)`, and category and brand are optional.
- No inventory, sales, purchase, or Angular catalog screens were added.
- Impacted modules: Catalog.
- Documentation updated: `DECISIONS.md` (ADR-010), `DOMAIN_MODEL.md`, `DATABASE.md`, `ARCHITECTURE.md`, `API_CONTRACTS.md`, `MODULE_CATALOG.md`, `TASKS.md`, `README.md`.

### 2026-10-08 — Catalog ownership design
- Recorded which module owns product identity, barcodes, current prices, stock, and locations.
- This design step added no tables or APIs. Catalog tables and APIs were added later on 2026-10-10 in `002_catalog.sql`.
- The open questions from this design were closed on 2026-10-10: prices are `numeric(12,2)`, minimum stock is `numeric(14,3)`, and category and brand are optional. Barcode is a Catalog capability, not a separate module.
- Impacted modules: Catalog, Inventory.
- Documentation updated: `DECISIONS.md` (ADR-010), `DOMAIN_MODEL.md`, `DATABASE.md`, `MODULE_CATALOG.md`, `TASKS.md`.

### 2026-10-07 — Dapper and Npgsql replace EF Core
- Removed EF Core, the Npgsql EF Core provider, `ApplicationDbContext`, and the EF migration classes.
- Added Dapper, the Npgsql driver, a connection factory, one-transaction execution, and ordered SQL scripts.
- Kept the existing Identity tables and seeded roles. Startup applies pending SQL scripts and checks the four shop roles.
- Impacted modules: Identity.
- Documentation updated: `DECISIONS.md` (ADR-009, ADR-008 revision), `ARCHITECTURE.md`, `DATABASE.md`, `DOMAIN_MODEL.md`, `AI_CONTEXT.md`, `MODULE_CATALOG.md`, `TASKS.md`, `AGENTS.md`, `README.md`.

### 2026-10-07 — Identity types moved out of Domain
- Moved `ApplicationUser`, `ApplicationRole`, and `ApplicationUserRole` to `src/Infrastructure/Identity`.
- Domain no longer references the ASP.NET Core Identity package. `ShopRoleNames` stays in Domain.
- No schema change and no new migration. The existing model snapshot type names were updated to match the moved classes.
- Impacted modules: Identity.
- Documentation updated: `DECISIONS.md` (ADR-008 revision), `ARCHITECTURE.md`, `DOMAIN_MODEL.md`, `DATABASE.md`, `MODULE_CATALOG.md`, `TASKS.md`.

### 2026-10-06 — PostgreSQL, EF Core, and Identity schema
- Added `ApplicationDbContext`, snake_case naming, base audit/soft-delete types, and ASP.NET Core Identity tables.
- Added the unapplied migration `InitialIdentityAndBaseSetup`. No business entities were added.
- Impacted modules: Identity.
- Documentation updated: `DATABASE.md`, `DECISIONS.md` (ADR-008), `DOMAIN_MODEL.md`, `ARCHITECTURE.md`, `MODULE_CATALOG.md`, `TASKS.md`.

### 2026-10-06 — Solution and Angular scaffold
- Added the .NET 8 solution (`src/Domain`, `src/Application`, `src/Infrastructure`, `src/Api`) and an Angular 21 application in `frontend`.
- No business entities, database migrations, or business API endpoints were added.
- Impacted modules: none yet.
- Documentation updated: `ARCHITECTURE.md`, `DECISIONS.md` (ADR-007), `TASKS.md`, `API_CONTRACTS.md`.

### 2026-10-05 — Initial AI context pack
- Established persistent requirements, architecture, domain map, module catalog and workflow rules.
- Established reuse-before-create and documentation synchronization rules.
