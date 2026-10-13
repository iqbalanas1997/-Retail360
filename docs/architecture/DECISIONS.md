# Architecture Decision Records

## ADR-001 — Modular monolith
Status: Accepted

Use a modular monolith for the initial system. The shop does not currently require distributed deployment, independent scaling, or service-to-service operations.

## ADR-002 — PostgreSQL
Status: Accepted

Use PostgreSQL as the primary relational database.

## ADR-003 — .NET 8 baseline
Status: Accepted

Use .NET 8 as the baseline because it is an LTS release. Upgrade only after an explicit decision.

## ADR-004 — Modern Angular
Status: Accepted

Use modern Angular. Do not use AngularJS 1.x.

## ADR-005 — Documentation as persistent AI context
Status: Accepted

Requirements, architecture, domain model, module catalog, decisions, tasks and changelog are persistent project knowledge. Cursor rules enforce reading and updating these documents.

## ADR-006 — Reuse before create
Status: Accepted

Before creating a class/component/service/endpoint/table, search for an existing equivalent and reuse or extend it when appropriate.

## ADR-007 — Solution layout and Angular 21
Status: Accepted

The solution file is `Retail360.sln` at the repository root. Projects live in `src/Domain`, `src/Application`, `src/Infrastructure`, and `src/Api`. The Angular application lives in `frontend`. `global.json` pins the .NET SDK to 8.0.412 with `latestFeature` roll-forward so a machine default of .NET 9 does not retarget the solution.

Project references follow the dependency rule: Application references Domain; Infrastructure references Application and Domain; Api references Application and Infrastructure. Domain references nothing.

The Angular application is version 21.2, using standalone components, SCSS, routing, `zone.js`, and Vitest. Angular 22 was not selected because it requires Node.js `^22.22.3`, `^24.15.0`, or `>=26`, and the available Node.js is 22.17.0.

Install frontend dependencies with npm 11.21.0 or newer. npm 10.9.2 fails while resolving peer dependencies (`Cannot read properties of null (reading 'edgesOut')`).

## ADR-008 — PostgreSQL naming and Identity schema
Status: Accepted

PostgreSQL identifiers use snake_case. SQL scripts write those names explicitly. The convention is fixed unless a later ADR changes it.

The shop role names Admin, Manager, Cashier, and Inventory Staff stay in Domain as `ShopRoleNames`. Those names are the business concept. The rows in `asp_net_roles` are the persistence of those names. Users and roles keep a string `id`, so they do not use `BaseEntity`. `asp_net_users` and `asp_net_roles` still carry `created_at`, `updated_at`, and `is_deleted`.

The four shop roles from the requirements are seeded as reference data. A Permission table is not part of this schema. Sign-in, cookies, and JWT are not configured here.

### 2026-10-07 — Revision
The original decision placed ASP.NET Core Identity classes in Domain. Those classes moved to Infrastructure, and Domain dropped the Identity stores package. A later decision, ADR-009, removed EF Core and the Identity class hierarchy. The PostgreSQL tables and seeded role rows were kept.

## ADR-009 — Dapper and Npgsql persistence
Status: Accepted

Retail360 persists data with Dapper and the Npgsql driver. EF Core is not used for application or business data, and it is not kept for Identity.

### Why Dapper
The shop's writes are explicit: a sale, a purchase, or a stock transfer changes several rows that must commit together. SQL makes those statements visible. EF Core change tracking, global query filters, and migrations are not required for that, and they were the only reason Domain and Infrastructure were tied to a framework persistence model before any business module existed.

### Why EF Core was removed
The solution had no business entities. EF Core existed to map ASP.NET Core Identity and to generate one migration. Keeping it would have made Identity the exception that pulls the ORM back into every later module. The persistence technology is therefore one stack: Dapper, Npgsql, and SQL scripts.

### PostgreSQL access
Infrastructure reads `ConnectionStrings:DefaultConnection` and creates connections through `NpgsqlConnectionFactory`. Credentials stay in configuration. Domain and Application do not reference Dapper or Npgsql.

Dapper maps snake_case columns to PascalCase properties with `DefaultTypeMap.MatchNamesWithUnderscores`.

### SQL migrations
Schema changes are ordered SQL scripts in `src/Infrastructure/Persistence/Sql/Migrations`. `SqlMigrationRunner` embeds those scripts, creates `schema_migrations` if needed, and applies each script once inside a transaction. The version is the script file name without `.sql`. A script that has been applied is not edited; a later change is a new script.

`001_initial_identity.sql` matches the existing Identity tables and is safe to run when those tables are already present. The API applies pending scripts at startup.

### Transactions
`PostgresTransaction.ExecuteAsync` opens one connection, begins one PostgreSQL transaction, runs the supplied work, and commits. A failure rolls the transaction back. A sale, purchase, or stock transfer uses this for its related inserts and stock changes. This is not a generic unit-of-work registry.

### Identity persistence
The existing Identity tables stay as they are. Retail360 does not implement ASP.NET Core Identity store interfaces. Those interfaces cover users, passwords, emails, lockout, stamps, claims, logins, tokens, and roles. Retail360 does not implement them. Sign-in uses Dapper on the existing tables, as ADR-012 records.

Dapper reads active roles from `asp_net_roles` through `ShopRoleQuery`. Startup fails if Admin, Manager, Cashier, or Inventory Staff is missing.

### Layer responsibilities
Application defines use cases and does not choose the database technology. Infrastructure owns connections, SQL, transactions, migration scripts, and Identity table access. Domain keeps business concepts such as `ShopRoleNames` and has no persistence attributes.

### Audit and soft delete
Dapper does not set timestamps or hide deleted rows. Insert and update SQL sets `created_at` and `updated_at`. A query that should hide deleted rows includes `is_deleted = false`. There is no global filter.

## ADR-010 — Catalog ownership
Status: Accepted

Catalog owns product identity: name, one unit, current purchase price, current selling price, minimum-stock threshold, active flag, and barcode values. Category and brand are optional. Unit is required. One product may have many barcodes in `product_barcodes`. A non-deleted barcode value belongs to one product. There is no separate Barcode module and no second barcode table.

`purchase_price` and `selling_price` are `numeric(12,2)`. `minimum_stock` is `numeric(14,3)`. C# uses `decimal`. These are current catalog values. A future purchase line stores the price paid, and a future sale line stores the price charged. Changing the product price does not change those documents.

Catalog does not own on-hand quantity, stock movements, area, shelf, supplier, purchase history, or sales history. `products` has no `stock`, `current_stock`, `available_quantity`, or shelf column. There is no SKU in the MVP. Categories are flat. A product has one unit and no unit conversion. Brand is `id`, `name`, and the audit columns, with no supplier and no logo.

`is_active` means the product is currently offered. `is_deleted` means it is logically removed. Deactivation changes only `is_active`. Soft delete changes `is_deleted` and is not a SQL `DELETE`. It is refused when that product has a `stock_balances` row or a `stock_movements` row. The check runs after the product row lock, in the same transaction as the update.

### 2026-10-10 — Approved
Price scale, minimum-stock scale, and optional category and brand are approved. Catalog backend uses `002_catalog.sql`.

### 2026-10-14 — Scale is checked, not rounded
`005_numeric_scale.sql` stores `purchase_price`, `selling_price`, and `minimum_stock` as unconstrained `numeric`. A price must be from 0 through `9999999999.99` and equal `round(price, 2)`. Minimum stock must be from 0 through `99999999999.999` and equal `round(minimum_stock, 3)`. The `numeric(12,2)` and `numeric(14,3)` limits remain the contract. The type modifier itself rounded `1.235` to `1.24` before a check ran. C# still uses `decimal`.

## ADR-011 — Inventory ledger and shelf balances
Status: Accepted

Inventory owns on-hand quantities, shelf locations, and the history of stock changes. Catalog keeps product identity, barcodes, current prices, and `minimum_stock`. The backend is implemented in `003_inventory.sql` and the Inventory API. Purchasing, Sales, and Inventory screens are not implemented.

### Location
The shop is one shop. Branches are out of scope. An area contains shelves, and a shelf belongs to one area. Stock is kept per product and shelf. A product may be on more than one shelf. Transfers between shelves are part of Inventory. Product does not gain a stock quantity or a shelf column.

### Ledger and balances
Two representations are kept together. An append-only movement ledger explains every change. A stored balance holds the current quantity for one product on one shelf. A stock operation inserts its movement rows and updates the affected balances in the same database transaction. If any part fails, the transaction rolls back.

A stored balance was chosen in addition to the ledger so current stock can be read without replaying history, while the ledger remains the explanation. A balance-only column was rejected because a quantity with no movement history cannot be audited. Summing the ledger on every read, with no stored balance, was not chosen: the approved design keeps the stored balance and checks it against the ledger.

Movement quantity is always greater than zero and must match `numeric(14,3)`: at most `99999999999.999` and equal to `round(quantity, 3)`. `005_numeric_scale.sql` stores it as unconstrained `numeric` so an extra digit is rejected. A balance at the maximum plus another valid quantity is rejected, and that operation is not stored. C# uses `decimal`. Direction is inbound or outbound. The tables are `areas`, `shelves`, `stock_balances`, and `stock_movements`. A balance is unique per product and shelf, and its quantity cannot be negative. A movement is not updated and is not soft-deleted. `stock_movements` has a `BEFORE UPDATE OR DELETE` trigger that rejects both changes, because the application connects as the table owner and a `REVOKE` would not hold.

Initial movement types are opening, adjustment, and transfer. Damaged, expired, and missing are adjustment reasons, not types. Opening stock is an inbound movement with product, shelf, quantity, time, and a required reason. It is not a purchase and it does not write a stock field on the product. An adjustment requires a reason. A transfer writes one outbound movement on the source shelf and one inbound movement on the destination shelf for the same quantity. Total shop stock does not change.

Purchasing and Sales are later modules. This decision does not define their document ids or line contracts.

### Concurrency and the zero floor
The operation locks the product row, records the client operation key, then locks the shelf rows and stored balance rows, in ascending shelf id order, inside the existing single-transaction boundary. After the product lock it rejects a soft-deleted product for a new key. The same key and payload returns the original movements. A different payload for that key is a conflict. Shelf soft-delete locks the shelf and rechecks history before deleting. Area soft-delete locks the area and its shelves, then rechecks shelves and history. Shelf creation locks the area. Catalog product soft-delete locks the same product row, rechecks stock balances and movements, and sets `is_deleted` only when that locked check finds neither. That lock order is the concurrency control. No isolation level is mandated. A second concurrent request must not pass the non-negative check. There is no permission to take a shelf below zero. The database constraint on the stored balance enforces the same floor. A balance check violation is reported as insufficient stock. Any other check violation on the stock write is a conflict without the database error text.

### History and locations
Corrections are new compensating movements with a reason. Ordinary location master data still uses `is_active` and `is_deleted`. A shelf or area is not soft-deleted when it has a balance or any movement history. An inactive shelf rejects inbound stock and allows outbound stock when the quantity is available. Deactivating a shelf leaves its balance and history in place. An inactive product may still be counted or adjusted. A soft-deleted product is rejected for a new movement.

### Low stock
Shop-wide on-hand quantity is the sum of that product's shelf balances, including quantity that remains on a deactivated shelf. Out of stock means that sum is zero. Low stock means that sum is greater than zero and less than or equal to Catalog `minimum_stock`. One shelf is not compared with the product threshold on its own.

### Reconciliation
For each product and shelf, inbound quantity minus outbound quantity must equal the stored balance. The two movements of a transfer must net to zero for the shop. `GET /api/inventory/reconciliation` returns the rows that do not match. The check is tested on `retail360_inventory_test`, not by deleting shop data.

## ADR-012 — Shop sign-in
Status: Accepted

Catalog and Inventory require an authenticated shop user. The API uses signed JWT bearer tokens. ASP.NET Core Identity store interfaces and EF Core stay unused. `PasswordHasher<TUser>` hashes passwords. The hash is stored in the existing `asp_net_users.password_hash` column.

`POST /api/auth/login` accepts `userName` and `password`. Unknown users, wrong passwords, soft-deleted users, and users locked out through `lockout_end` all receive the same 401 message. The schema has no separate inactive flag. A wrong password for an existing, unlocked user increments `access_failed_count` in one update. The fifth failure sets `lockout_end` five minutes ahead and resets the count. A later success clears the count only when that same update still sees the account as unlocked, so a concurrent lock is not removed. There is no public registration and no anonymous user-management endpoint.

The access token lifetime defaults to 15 minutes and must be from 1 to 60. Validation checks the signature, issuer, audience, and expiry, with no clock skew. The token may repeat the user's roles, but each request loads roles from `asp_net_user_roles`. A role that is only present in the token does not authorize the call. A soft-deleted or locked user is rejected on the next request.

`Authentication:SigningKey` is required and must be at least 32 bytes. It is not stored in source control. A missing or short key stops startup before migrations run. The first Admin is created only when `Authentication:Bootstrap:UserName` and `Authentication:Bootstrap:Password` are both set and no active Admin exists. Creation locks the Admin role row, rechecks, and inserts in that transaction, so two startups do not create two Admins. Those values are environment or user-secret settings, not tracked files. Remove them after the Admin exists. The API does not log passwords, password hashes, signing keys, or bearer tokens. Outside this machine, the site must be served over HTTPS. Development Swagger is not an access check.

Admin and Manager may change the catalog. Admin, Manager, and Inventory Staff may change areas, shelves, and stock. All four shop roles may read Catalog and Inventory. Cashier cannot change prices or stock. This is a single-shop baseline: no multi-factor authentication, no external identity provider, and no permission table. It is not a claim of production readiness.
