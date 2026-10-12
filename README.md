# Retail360

Grocery shop management system for a small retail shop. The counter workflow is the priority: find a product, enter a quantity, review the cart, take payment, and show a receipt.

The repository contains the foundation, the Catalog backend, and the Inventory backend. Catalog covers categories, brands, units, products, and barcodes. Inventory covers areas, shelves, shelf balances, and the append-only stock ledger. Sales, purchases, and the Angular screens are not implemented yet.

## Stack

- .NET 8 Web API
- Angular 21.2
- PostgreSQL
- Dapper
- Npgsql

EF Core is not used. `global.json` pins the .NET SDK to 8.0.412. The Angular app expects Node.js 20.19+, 22.12+, or 24+, and npm 11.21.0 or newer.

## Layout

| Path | Role |
|---|---|
| `src/Domain` | Domain concepts. No Dapper, Npgsql, EF Core, or API packages. |
| `src/Application` | Use cases. References Domain only. |
| `src/Infrastructure` | PostgreSQL connection, SQL migrations, transactions, and Identity data access. |
| `src/Api` | HTTP host and composition root. |
| `frontend` | Angular application. |
| `docs` | Requirements, architecture, and project status. |

Dependency direction: API → Application → Domain, and Infrastructure → Application and Domain.

## Run locally

1. Install the .NET 8 SDK, Node.js, npm 11.21.0 or newer, and PostgreSQL.
2. Create a local database named `retail360`.
3. Set `ConnectionStrings:DefaultConnection` in `src/Api/appsettings.json`. The checked-in value is a local development placeholder.
4. Start the API. Startup applies pending SQL scripts in `src/Infrastructure/Persistence/Sql/Migrations` and checks that the four shop roles exist.

```powershell
dotnet run --project src/Api/Retail360.Api.csproj --launch-profile http
```

Swagger is at [http://localhost:5117/swagger](http://localhost:5117/swagger).

5. Start the Angular app:

```powershell
cd frontend
npm install
npm start
```

The UI is at [http://localhost:4200](http://localhost:4200).

## Build

```powershell
dotnet build Retail360.sln
dotnet test Retail360.sln
```

```powershell
cd frontend
npm run build
```

## Persistence

PostgreSQL is accessed with Dapper and Npgsql from `src/Infrastructure`. Schema changes are numbered SQL scripts in `src/Infrastructure/Persistence/Sql/Migrations`. The API applies pending scripts at startup and records them in `schema_migrations`.

The existing Identity tables are kept. Seeded roles are Admin, Manager, Cashier, and Inventory Staff. Sign-in is not implemented. New SQL should use snake_case and set `created_at`, `updated_at`, and `is_deleted` explicitly. There is no EF Core global query filter.

## Current status

Identity tables and the four shop roles are in place. The Catalog API covers categories, brands, units, and products, including activation, deactivation, logical delete, and barcode lookup. The Inventory API covers areas, shelves, opening stock, adjustments, transfers, balances, movement history, low stock, and reconciliation. Opening stock, adjustments, and transfers require a client `operationId`. Repeating the same id and payload returns the original movements. `002_catalog.sql`, `003_inventory.sql`, and `004_stock_operations.sql` are applied by the migration runner. Product has no stock quantity. Sign-in, permissions, and the Angular screens are still to be built. See `docs/project/TASKS.md` for the task board.
