# Database Conventions

## Database
PostgreSQL, accessed with Dapper and Npgsql. See ADR-009. EF Core is not used.

## Money
Use `numeric/decimal` with explicit precision/scale appropriate to the business.

## Inventory
Do not rely only on a mutable stock field. Maintain auditable stock movements and derive/maintain balances consistently.

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

There is no `Permission` table yet. The application does not implement ASP.NET Core Identity stores. `ShopRoleQuery` reads roles that are not soft-deleted.

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
