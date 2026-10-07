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
The existing Identity tables stay as they are. Retail360 does not implement ASP.NET Core Identity store interfaces. Those interfaces cover users, passwords, emails, lockout, stamps, claims, logins, tokens, and roles, and the API does not sign anyone in yet. Building that store now would be an authentication framework ahead of the authentication baseline.

Dapper reads active roles from `asp_net_roles` through `ShopRoleQuery`. Startup fails if Admin, Manager, Cashier, or Inventory Staff is missing. User sign-in is still a later task and will use these tables rather than a new user model.

### Layer responsibilities
Application defines use cases and does not choose the database technology. Infrastructure owns connections, SQL, transactions, migration scripts, and Identity table access. Domain keeps business concepts such as `ShopRoleNames` and has no persistence attributes.

### Audit and soft delete
Dapper does not set timestamps or hide deleted rows. Insert and update SQL sets `created_at` and `updated_at`. A query that should hide deleted rows includes `is_deleted = false`. There is no global filter.
