# Project Knowledge Changelog

Use this file for meaningful project-level changes that affect future AI context.

## Format
### YYYY-MM-DD — Title
- What changed
- Why
- Impacted modules
- Documentation updated

## Entries
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
