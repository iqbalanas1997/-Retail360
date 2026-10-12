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

Barcode values belong to Catalog. There is no separate Barcode module. Modules may share stable domain concepts, but do not duplicate them.

## Inventory
Inventory owns areas, shelves, on-hand balances, and stock movements. See ADR-011. The backend is implemented.

An area contains shelves. Stock is stored per product and shelf. Catalog does not store that quantity. The movement ledger is append-only and explains every change. The stored balance is the current quantity for one product on one shelf. Both are written in one transaction. A failure rolls the whole operation back.

The operation locks the product row, records the client operation key, then locks the affected shelf rows and balance rows, in ascending shelf id order, before it changes them. Shelf soft-delete locks the shelf and rechecks stock history before it marks the shelf deleted. Area soft-delete locks the area and its shelves, then rechecks. Shelf creation locks the area and rejects a soft-deleted area before it inserts. Catalog soft-delete locks the product row before it marks the product deleted. A shelf balance cannot go below zero. A transfer moves one positive quantity from one shelf to another and does not change the shop total. Opening stock and adjustments are movements. Damaged, expired, and missing are adjustment reasons. An inactive shelf rejects inbound stock and allows a valid outbound movement. Purchasing and Sales are not part of this module.

Opening stock, an adjustment, and a transfer require a client-generated `operationId`. Migration `004_stock_operations.sql` stores that id and a fingerprint of the normalized payload in `stock_operations`. The same operation id and an equivalent payload return the original movements and do not change stock again. Reusing the id with a different payload returns HTTP 409. The operation row, movements, and balance updates commit or roll back together.

Shop-wide on-hand quantity is the sum of the product's shelf balances. Low stock compares that sum with Catalog `minimum_stock`.

## Repository layout
See ADR-007.

- `Retail360.sln` at the repository root.
- `global.json` pins the local .NET SDK to 8.0.x.
- `src/Domain` — class library with no project references and no framework packages. Shop role names live here.
- `src/Application` — references Domain. It does not reference Dapper, Npgsql, or EF Core.
- `src/Infrastructure` — references Application and Domain. It owns the PostgreSQL connection, SQL migrations, transactions, and Identity table access.
- `src/Api` — Web API composition host. References Application and Infrastructure.
- `frontend` — Angular application.

Catalog is implemented in Domain, Application, Infrastructure, and the API. Its schema script is `002_catalog.sql`. Inventory is implemented in the same layers. Its schema scripts are `003_inventory.sql` and `004_stock_operations.sql`. Do not edit `001_initial_identity.sql`, `002_catalog.sql`, `003_inventory.sql`, or `004_stock_operations.sql`. Authentication and authorization are not implemented. Sales, purchases, and the Angular screens are not implemented. See ADR-009, ADR-010, and ADR-011.
