# Project Task Board

## Current phase
Phase 0 — Foundation and architecture. The Catalog backend is implemented and verified. The Inventory design is approved and documented in ADR-011. Inventory code and the Angular screens are not started.

## Rules
- Only one primary task should be actively implemented at a time unless parallel work is explicitly planned.
- Each task must identify impacted modules.
- Mark tasks complete only after verification.
- Do not implement future-scope features merely because they appear in requirements.

## Backlog

### Foundation
- [x] Create .NET 8 solution and layer structure.
- [x] Create modern Angular application.
- [x] Configure PostgreSQL and EF Core.
- [x] Keep Domain free of framework packages. Shop role names stay in Domain.
- [x] Replace EF Core with Dapper and Npgsql. Keep the existing Identity tables.
- [ ] Establish authentication/authorization baseline.
- [ ] Establish shared error/validation/logging conventions.
- [ ] Add initial CI/build/test commands.

### Catalog
- [x] Product
- [x] Category
- [x] Brand
- [x] Unit
- [x] Barcode
- [x] Verify the Catalog backend: build, automated tests, and migration repeatability
- [ ] Catalog screens

### Inventory
- [x] Record the approved design: shelf balances, append-only movements, opening stock, adjustments, transfers, and shop-wide low stock.
- [ ] Implement areas and shelves.
- [ ] Implement stock balances and append-only movements.
- [ ] Implement opening stock, adjustments, and transfers in one transaction per operation.
- [ ] Enforce the non-negative shelf balance, including concurrent updates.
- [ ] Identify low stock and out of stock from the sum of shelf balances.
- [ ] Add a new SQL migration. Do not edit `001_initial_identity.sql` or `002_catalog.sql`.
- [ ] Add domain tests, service tests, and PostgreSQL tests on an isolated database.
- [ ] Add the reconciliation check that stored balances match movement history.

### Sales / POS
- [ ] Product scan/search
- [ ] Cart
- [ ] Quantity
- [ ] Cash/credit payment
- [ ] Stock reduction
- [ ] Receipt
- [ ] Sales history

### Purchases
- [ ] Purchase entry
- [ ] Purchase lines
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
Wait for explicit approval before implementing Inventory. The design is documented and no Inventory code exists. Angular catalog screens are still not started. Authentication is still not implemented. Sign-in and permission checks do not exist.
