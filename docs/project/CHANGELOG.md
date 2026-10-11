# Project Knowledge Changelog

Use this file for meaningful project-level changes that affect future AI context.

## Format
### YYYY-MM-DD — Title
- What changed
- Why
- Impacted modules
- Documentation updated

## Entries
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
