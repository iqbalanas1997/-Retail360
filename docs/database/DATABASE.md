# Database Conventions

## Database
PostgreSQL.

## Money
Use `numeric/decimal` with explicit precision/scale appropriate to the business.

## Inventory
Do not rely only on a mutable stock field. Maintain auditable stock movements and derive/maintain balances consistently.

## Naming
Snake case is the fixed convention for PostgreSQL tables, columns, keys, foreign keys, and indexes. See ADR-008.

Examples: `asp_net_users`, `created_at`, `pk_asp_net_users`, `fk_asp_net_user_roles_asp_net_users_user_id`.

`ApplicationDbContext` applies this naming in `OnModelCreating`. Do not introduce a second naming style.

## Identity schema
The initial migration `InitialIdentityAndBaseSetup` creates only the ASP.NET Core Identity tables:

- `asp_net_users`
- `asp_net_roles`
- `asp_net_user_roles`
- `asp_net_user_claims`
- `asp_net_role_claims`
- `asp_net_user_logins`
- `asp_net_user_tokens`

`asp_net_users` and `asp_net_roles` include `created_at`, `updated_at`, and `is_deleted`. Claim, login, token, and user-role tables are the Identity store tables. They are not separate business concepts. The CLR types for users, roles, and user-roles are in `src/Infrastructure/Identity`. Moving those types did not change these tables.

`BaseEntity` is not a table. Future business entities that extend it use `uuid` for `id`. Identity users and roles keep Identity's string `id`, so they do not extend `BaseEntity`.

Deleting an `ISoftDeletable` entity in `SaveChanges` sets `is_deleted` instead of removing the row. `ApplicationUser` and `ApplicationRole` also have a global query filter that hides `is_deleted` rows. There is no `Permission` table yet.

Seeded roles, as stable reference data: Admin, Manager, Cashier, Inventory Staff.

## Local connection
`src/Api/appsettings.json` key `ConnectionStrings:DefaultConnection` points at a local PostgreSQL database named `retail360`. The username and password there are a local development placeholder, not a production secret.

## Migrations
- One logical change per migration where practical.
- Never rewrite an already-applied migration to fix a later requirement.
- Review generated migration SQL for destructive operations.
- Seed only stable reference data; do not seed transactional business data casually.
