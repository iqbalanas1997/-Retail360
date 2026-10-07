# Project Task Board

## Current phase
Phase 0 — Foundation and architecture. Solution structure, the Angular application, and PostgreSQL/EF Core infrastructure are in place.

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
- [x] Keep Domain free of the ASP.NET Core Identity package. Identity types live in Infrastructure.
- [ ] Establish authentication/authorization baseline.
- [ ] Establish shared error/validation/logging conventions.
- [ ] Add initial CI/build/test commands.

### Catalog
- [ ] Product
- [ ] Category
- [ ] Brand
- [ ] Unit
- [ ] Barcode

### Inventory
- [ ] Areas/Shelves
- [ ] Stock balances
- [ ] Stock movements
- [ ] Transfers
- [ ] Adjustments
- [ ] Low/out-of-stock rules

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
Establish authentication/authorization baseline. Not started. Identity tables, role seed, and `AddIdentityCore` registration exist. Sign-in and permission checks do not.
