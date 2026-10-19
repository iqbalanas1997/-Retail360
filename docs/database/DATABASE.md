# Database Conventions

## Database
PostgreSQL, accessed with Dapper and Npgsql. See ADR-009. EF Core is not used.

## Money
Use `numeric` and C# `decimal`. Do not use floating point. Prices stay inside the `numeric(12,2)` limits: zero through `9999999999.99`, with at most 2 fractional digits. `005_numeric_scale.sql` stores the column as unconstrained `numeric` and checks `value = round(value, 2)`. A `numeric(12,2)` column would round `1.235` to `1.24` before a check could reject it.

## Inventory
`003_inventory.sql` creates the Inventory schema. `004_stock_operations.sql` adds the stock operation key and an index on `stock_movements.other_shelf_id`. `005_numeric_scale.sql` stops PostgreSQL from rounding extra fractional digits. Do not edit `001_initial_identity.sql`, `002_catalog.sql`, `003_inventory.sql`, or `004_stock_operations.sql`. The next change is a new numbered script. Product master data does not include `stock`, `current_stock`, `available_quantity`, or a shelf column.

On-hand quantity is a stored balance per product and shelf. Every change also inserts an append-only movement in the same transaction. Quantity uses C# `decimal` and must match `numeric(14,3)`: greater than zero for a movement, at least zero for a balance, at most `99999999999.999`, and equal to `round(value, 3)`. Direction is `in` or `out`. There is no negative-stock override. A shelf total above the maximum is rejected and the operation row is not kept.

- `areas` and `shelves`. A shelf references one area. Both are master data with `is_active`, `created_at`, `updated_at`, and `is_deleted`. Area names are unique among non-deleted rows, case-insensitively. Shelf names are unique per area among non-deleted rows.
- `stock_balances`. One row per product and shelf, unique on `(product_id, shelf_id)`. `quantity` is `numeric`, must be `>= 0`, must be `<= 99999999999.999`, and must equal `round(quantity, 3)`. Foreign keys reference `products` and `shelves` with no cascade delete. A balance row is not soft-deleted.
- `stock_movements`. Append-only. Columns are `id`, `operation_id`, `product_id`, `shelf_id`, `quantity numeric`, `direction`, `movement_type` (`opening`, `adjustment`, or `transfer`), `reason`, `other_shelf_id`, `occurred_at`, and `created_at`. Movement quantity is `> 0`, `<= 99999999999.999`, and equal to `round(quantity, 3)`. A movement has no `updated_at` and no `is_deleted`. Opening must be inbound. A transfer requires a different `other_shelf_id`. Other types must leave `other_shelf_id` null. Trigger `stock_movements_append_only` rejects `UPDATE` and `DELETE`.

Indexes include `uq_areas_name`, `uq_shelves_area_name`, `ix_shelves_area_id`, `ix_stock_balances_product_id`, `ix_stock_balances_shelf_id`, `ix_stock_movements_product_id`, `ix_stock_movements_shelf_id`, `ix_stock_movements_operation_id`, and `ix_stock_movements_other_shelf_id`. The other-shelf index supports history checks that match `other_shelf_id` during shelf and area deletion.

`stock_operations` stores one row per client `operationId`: `id`, `product_id`, `fingerprint`, and `created_at`. `id` is the primary key. The fingerprint is the normalized stock request. A repeat of that request returns the movements already stored for the key. A different request using the same key is rejected. The operation row, balance updates, and movement inserts commit or roll back together.

A stock operation locks the product row, then records the operation key, then locks the affected shelf rows in ascending shelf id order, then the balance rows in that same order. It rechecks that the product is not soft-deleted after the product lock when the key is new. It then writes the movements and balance updates and reads those movements before commit. A transfer does this for both shelves, and the shop total is unchanged. The first movement for a product-shelf pair creates the balance row at zero before applying the delta. Shelf soft-delete locks that shelf row, rechecks balances and movements, and deletes only when that locked check finds none. Area soft-delete locks the area row, then the shelves in that area in ascending id order, and deletes the area only when that locked check finds no non-deleted shelf, balance, or movement. Shelf creation locks the area row first. Catalog product update and soft-delete lock the product row before changing it. Soft-delete then rechecks balances and movements under that lock and refuses the update when either exists. Balance reads still return a shelf quantity when the area row is soft-deleted, so stock does not disappear from the balance list.

Reconciliation: for every product and shelf, inbound quantity minus outbound quantity equals `stock_balances.quantity`. The two rows of a transfer net to zero. `GET /api/inventory/reconciliation` returns mismatches.

An inactive shelf rejects inbound stock and allows outbound stock that does not go below zero. A shelf or area with a balance or any movement is not soft-deleted. A product with a balance row or any movement is not soft-deleted. A soft-deleted product is not a valid target. An inactive product remains a valid target. Shop-wide on-hand quantity sums every shelf balance for the product, including a deactivated shelf, and is compared with `products.minimum_stock`.

## Catalog schema
`002_catalog.sql` adds `categories`, `brands`, `units`, `products`, and `product_barcodes`. Do not edit `001_initial_identity.sql`.

Identifiers are `uuid`. Each table has `created_at`, `updated_at`, and `is_deleted`. Soft delete is an update. Category, brand, and unit names are unique where `is_deleted = false`, compared case-insensitively. Product names are not unique. `products.category_id` and `products.brand_id` are nullable. `products.unit_id` is required. Prices and `minimum_stock` are unconstrained `numeric` with the scale checks from `005_numeric_scale.sql`. `product_barcodes.barcode` is unique where `is_deleted = false`, matched exactly. `products` has no stock, SKU, or shelf column.

## Suppliers schema
`006_suppliers.sql` adds `suppliers`. Do not edit `001_initial_identity.sql`, `002_catalog.sql`, `003_inventory.sql`, `004_stock_operations.sql`, or `005_numeric_scale.sql`.

## Purchasing schema
`007_purchases.sql` adds `purchases` and `purchase_lines`, and extends the stock movement type check to include `purchase`. Do not edit `001` through `006`.

A purchase stores the supplier, purchase date, optional reference, optional notes, `recorded` or `received` status, server total, receive operation id, receive fingerprint, and the audit columns. A received purchase cannot be soft-deleted. A line stores the product, line number, quantity, unit cost, line total, and the shelf assigned when the goods are received. Quantity follows the `numeric(14,3)` checks. Money follows the `numeric(12,2)` checks. The line total must equal `round(quantity * unit_cost, 2)`.

`suppliers` columns are `id`, `name`, `contact_person`, `phone`, `email`, `address`, `notes`, `is_active`, `created_at`, `updated_at`, and `is_deleted`. The name is required and at most 200 characters. Contact person is optional and at most 200. Phone is optional and at most 40. Email is optional and at most 200, and when present it is one address. Address is optional and at most 400. Notes are optional and at most 1000. `uq_suppliers_name` is unique on `lower(name)` where `is_deleted = false`. There is no `shop_id`, payment balance, or purchase foreign key.

## Naming
Snake case is the fixed convention for PostgreSQL tables, columns, keys, foreign keys, and indexes. See ADR-008.

Examples: `asp_net_users`, `created_at`, `pk_asp_net_users`, `fk_asp_net_user_roles_asp_net_users_user_id`.

SQL scripts write these names explicitly. Do not introduce a second naming style.

## Connection
`src/Api/appsettings.json` key `ConnectionStrings:DefaultConnection` points at a local PostgreSQL database named `retail360`. The username and password there are a local development placeholder, not a production secret. Infrastructure reads that setting. Source code does not hard-code credentials.

## Identity schema
The initial script `001_initial_identity.sql` describes the existing ASP.NET Core Identity tables:

- `asp_net_users`
- `asp_net_roles`
- `asp_net_user_roles`
- `asp_net_user_claims`
- `asp_net_role_claims`
- `asp_net_user_logins`
- `asp_net_user_tokens`

`asp_net_users` and `asp_net_roles` include `created_at`, `updated_at`, and `is_deleted`. Claim, login, token, and user-role tables are Identity store tables. They are not separate business concepts.

`BaseEntity` is not a table. Future business entities that extend it use `uuid` for `id`. Identity users and roles keep a string `id`, so they do not extend `BaseEntity`.

Seeded roles, as stable reference data: Admin, Manager, Cashier, Inventory Staff. Their ids are unchanged:

| Role | Id |
|---|---|
| Admin | `8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e01` |
| Manager | `8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e02` |
| Cashier | `8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e03` |
| Inventory Staff | `8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e04` |

There is no `Permission` table. The application does not implement ASP.NET Core Identity stores. `ShopRoleQuery` reads roles that are not soft-deleted. Sign-in reads `asp_net_users.user_name`, `password_hash`, `is_deleted`, `lockout_enabled`, `lockout_end`, and `access_failed_count`, and loads role names through `asp_net_user_roles`. Five wrong passwords set `lockout_end`. No migration was added for sign-in. The password hash is never returned by the API.

The database may still contain `__EFMigrationsHistory` from the removed EF Core migration. The application does not read or write that table. Do not drop the Identity tables to remove it.

## Audit columns and soft delete
Dapper does not track changes or apply a global filter.

- An insert sets `created_at` and `updated_at`.
- An update sets `updated_at`.
- A soft delete sets `is_deleted` to true and sets `updated_at`. It does not remove the row.
- A query that must hide deleted rows includes `is_deleted = false`.

## Transactions
One business operation that changes several rows uses `PostgresTransaction.ExecuteAsync`. That opens one connection and one PostgreSQL transaction, then commits or rolls back. Do not add a generic unit-of-work type for this.

## Migrations
Scripts live in `src/Infrastructure/Persistence/Sql/Migrations` and are embedded in the Infrastructure assembly. `SqlMigrationRunner` applies them in file-name order and records each version in `schema_migrations`.

- One logical change per script where practical.
- Do not edit a script after it has been applied. Add the next numbered script.
- Scripts for tables that may already exist must be safe to run again until their version is recorded. `001_initial_identity.sql` uses `create table if not exists` and inserts roles with `on conflict do nothing`.
- The API applies pending scripts at startup.
- Review SQL for destructive operations.
- Seed only stable reference data. Do not seed transactional business data casually.
