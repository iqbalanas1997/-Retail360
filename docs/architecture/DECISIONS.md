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

PostgreSQL identifiers use snake_case. That convention is applied by `ApplicationDbContext` and is fixed unless a later ADR changes it.

`ApplicationUser`, `ApplicationRole`, and `ApplicationUserRole` live in `src/Infrastructure/Identity`. They are ASP.NET Core Identity implementation types. Domain does not reference `Microsoft.Extensions.Identity.Stores`, EF Core, Npgsql, or the Web API. `ApplicationUser` and `ApplicationRole` do not inherit `BaseEntity` because ASP.NET Identity uses a string id. They still carry `CreatedAt`, `UpdatedAt`, and `IsDeleted`.

The shop role names Admin, Manager, Cashier, and Inventory Staff stay in Domain as `ShopRoleNames`. Those names are the business concept. The Identity role rows are the persistence of those names.

The four shop roles from the requirements are seeded as reference data. A Permission table is not part of this schema. Sign-in, cookies, and JWT are not configured here.

### 2026-10-07 — Revision
The original decision placed the Identity classes in Domain so they could be reused without a second user model. That required Domain to reference the Identity stores package. The classes now live in Infrastructure. The database tables and the applied migration id are unchanged. The EF model snapshot uses the Infrastructure type names so Entity Framework does not treat the move as a new set of tables.
