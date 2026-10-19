# Grocery Shop Management System — Agent Instructions

## Mission
Build a simple, reliable Grocery Shop Management System for a small retail shop.

## Technology baseline
- Backend: .NET 8 Web API (LTS baseline)
- Frontend: modern Angular (NOT legacy AngularJS 1.x)
- Database: PostgreSQL
- Persistence: Dapper + Npgsql. Do not use EF Core.
- API style: REST/JSON
- Architecture: modular monolith with clear Domain/Application/Infrastructure/API boundaries
- UI priority: extremely simple counter/POS workflow, minimal screens and controls.

## Source-of-truth order
1. `docs/requirements/REQUIREMENTS.md`
2. `docs/architecture/ARCHITECTURE.md`
3. `docs/domain/DOMAIN_MODEL.md`
4. `docs/architecture/DECISIONS.md`
5. `docs/project/MODULE_CATALOG.md`
6. `docs/project/TASKS.md`
7. Existing source code and database migrations
8. Chat instructions for the current task

If documentation conflicts with existing code, do NOT silently redesign the system. Explain the conflict and propose the smallest safe change.

## Non-negotiable agent behavior
- Search the existing codebase before creating a new class, component, service, endpoint, DTO, repository, helper, table, or migration.
- Reuse existing abstractions and patterns whenever they fit.
- Never create duplicate functionality under a different name.
- Never invent requirements. Mark assumptions explicitly.
- Before a multi-file change, make a short implementation plan.
- Keep changes focused on the requested task; do not perform unrelated refactoring.
- Do not change architecture or technology without explicit approval.
- After changes, run the relevant build/tests and report what was verified.
- Update project documentation when an architectural decision, module, API, database structure, or workflow changes.
- Preserve existing behavior unless the task explicitly changes it.
- Prefer the simplest implementation that satisfies the requirement.

## Context continuity
At the start of a substantial task:
1. Read this file.
2. Read the relevant requirement/architecture/domain docs.
3. Inspect existing code before proposing new code.
4. Check `docs/project/TASKS.md` and `docs/project/MODULE_CATALOG.md`.
5. State which existing modules/patterns will be reused.
6. At the end, update task status and relevant documentation.

## Stop conditions
Stop and ask for clarification when:
- a requirement is ambiguous and the choice changes business behavior;
- two existing patterns conflict;
- a destructive database change is required;
- a new dependency is needed but not already approved;
- a request would violate the architecture or create duplicate concepts.

## UI principle
The POS/counter workflow must remain fast and simple:
- scan/search product
- enter quantity
- review cart
- take payment
- print/show receipt

Avoid unnecessary popups, fields, navigation and visual noise.

## Catalog
Catalog owns Category, Brand, Unit, Product, and ProductBarcode. Category and brand are optional. Unit is required. Barcode values live in `product_barcodes`; there is no separate Barcode module or second barcode table. The MVP product identity is the product id, name, and barcode values. There is no SKU. Product does not store a stock quantity. `purchase_price` and `selling_price` are current catalog prices within the `numeric(12,2)` limits. `minimum_stock` is within the `numeric(14,3)` limits. Extra fractional digits are rejected. `is_active` means the product is offered. `is_deleted` means it is logically removed. Deactivation does not delete the product. A product with a stock balance row or a stock movement cannot be soft-deleted, even when the current quantity is zero.

## Identity
Sign-in uses the existing `asp_net_users` and `asp_net_user_roles` tables. Passwords are hashed. Five wrong passwords lock the user for five minutes through `access_failed_count` and `lockout_end`. A short-lived JWT carries the user id. Each request loads that user's roles from the database, so a role claim in the token is not authority. Catalog writes are Admin and Manager. Inventory changes and stock posts are Admin, Manager, and Inventory Staff. Cashier can read both modules. There is no public registration. The first Admin is created only when bootstrap settings are supplied and no active Admin exists. That creation locks the Admin role row. Authentication is not production-ready until a deployment supplies its own signing key outside source control and serves the API over HTTPS.

## Inventory
Inventory owns areas, shelves, the on-hand balance per product and shelf, and the append-only movement ledger. The backend is implemented in ADR-011, `003_inventory.sql`, `004_stock_operations.sql`, and `005_numeric_scale.sql`. Do not add a stock quantity or a shelf column to Product. Movement quantity is a positive `decimal` within the `numeric(14,3)` limits. Extra fractional digits are rejected. A shelf balance cannot go below zero. Movements are not edited or soft-deleted. A transfer does not change total shop stock. An inactive shelf rejects inbound stock and allows outbound stock. Low stock compares the sum of shelf balances, including inactive shelves, with Catalog `minimum_stock`. Each stock change requires a client operation id. The same id and payload is one operation. Do not implement Purchasing or Sales as part of Inventory. A purchase receipt is a `purchase` movement posted by Purchasing through this ledger.

## Suppliers
Suppliers owns the supplier record: name, optional contact person, phone, email, address, and notes, plus `is_active`. Names are unique among non-deleted rows, compared case-insensitively. A soft-deleted name can be used again. An inactive supplier can still be edited. Deactivation is not deletion. There is no supplier payment or payable balance in this module. A purchase references the supplier id. A supplier referenced by any purchase row cannot be soft-deleted. Admin and Manager can change suppliers. Admin, Manager, Cashier, and Inventory Staff can read them. Do not reuse the Catalog write policy for that check.

## Purchasing
Purchasing owns the purchase document and its lines. A recorded purchase does not change stock. Receiving posts one inbound `purchase` movement per line through the Inventory ledger in the same transaction. The server calculates line totals and the document total. A new purchase requires an active supplier. An inactive product can still be purchased. An inactive shelf cannot receive the goods. Admin and Manager record purchases. Admin, Manager, and Inventory Staff receive them. All four shop roles can read them. There is no supplier payment, return, or cancellation in this module.

## Sales
Sales owns the completed cash sale and its lines. Completing a sale posts one outbound `sale` movement per line through the Inventory ledger in the same transaction. The server copies the product name and Catalog selling price and calculates the totals. The caller names the shelf the goods leave. An inactive product cannot be sold. A missing or soft-deleted product is rejected. An inactive shelf can be sold from. Admin, Manager, and Cashier complete sales. All four shop roles can read them. Inventory Staff cannot sell. There is no credit, return, refund, discount, or cancellation in this module. The Angular counter is `/pos`, and sales history is `/sales`.
