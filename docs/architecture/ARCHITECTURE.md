# Architecture

## Baseline
Use a modular monolith. Do not split into microservices unless a future requirement proves it necessary.

## Layers
- Domain: entities, value objects, domain rules.
- Application: use cases, commands/queries, DTOs, validation, interfaces.
- Infrastructure: Dapper, Npgsql, PostgreSQL, persistence, external services.
- API: HTTP endpoints, authentication/authorization, composition.
- Angular Web: presentation and user interaction.

## Dependency rule
API -> Application -> Domain
Infrastructure -> Application/Domain
Domain must remain independent of API and Infrastructure.

## Business modules
Keep module boundaries clear:
- Catalog
- Inventory
- Sales
- Purchasing
- Suppliers
- Customers
- Expenses
- Reporting
- Identity/Users
- Audit
- Barcode

Modules may share stable domain concepts, but do not duplicate them.

## Important design principle
Inventory is not just a Product.Stock integer. Stock changes must be explainable through stock movements/transactions.

## Repository layout
See ADR-007.

- `Retail360.sln` at the repository root.
- `global.json` pins the local .NET SDK to 8.0.x.
- `src/Domain` — class library with no project references and no framework packages. Shop role names live here.
- `src/Application` — references Domain. It does not reference Dapper, Npgsql, or EF Core.
- `src/Infrastructure` — references Application and Domain. It owns the PostgreSQL connection, SQL migrations, transactions, and Identity table access.
- `src/Api` — Web API composition host. References Application and Infrastructure.
- `frontend` — Angular application.

Business modules other than the Identity schema are not part of this layout. The initial schema script is `001_initial_identity.sql`. See ADR-009.
