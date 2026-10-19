# Retail360

Grocery shop management system for a small retail shop. The counter workflow is the priority: find a product, enter a quantity, review the cart, take payment, and show a receipt.

The repository contains the foundation, the Catalog, Inventory, Suppliers, and Purchasing backends, and Angular screens for sign-in, Catalog, Inventory, Suppliers, and Purchases. Catalog covers categories, brands, units, products, and barcodes. Inventory covers areas, shelves, shelf balances, and the append-only stock ledger. The Angular app has sign-in, Catalog screens, Inventory screens for stock, locations, opening stock, adjustments, and transfers, Supplier screens, and Purchase screens for the purchase list, a recorded purchase, and receiving. An expired sign-in returns the open screen to the login page. Stock posts can search products by name. Supplier payments are not implemented yet.

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
2. Create a local development database named `retail360_dev`. Leave the shop database `retail360` unchanged. Automated tests use their own databases and are not a development target.
3. Set the local database password and the JWT signing key outside source control. `src/Api/appsettings.json` keeps an empty password and does not contain a signing key. From `src/Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=retail360_dev;Username=postgres;Password=<local-password>"
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$signingKey = [Convert]::ToBase64String($bytes)
dotnet user-secrets set "Authentication:SigningKey" $signingKey
```

The same values can be environment variables: `ConnectionStrings__DefaultConnection` and `Authentication__SigningKey`. The signing key must be at least 32 bytes. A missing or short key stops the API before it applies migrations. Tests read `RETAIL360_DB_PASSWORD` when the tracked password is empty. Do not commit either secret.

4. Optional first Admin. There is no public registration endpoint. Set `Authentication__Bootstrap__UserName` and `Authentication__Bootstrap__Password` before startup only when no active Admin exists. The password must be at least 8 characters. Startup locks the Admin role, rechecks, and creates at most one Admin. It does not log the password. After the user exists, remove both bootstrap variables from the environment and from user secrets. Do not put them in `appsettings.json`.

Outside this computer, host the API behind HTTPS. The process redirects HTTP except in the test host. Development Swagger does not authenticate callers. The API does not write passwords, password hashes, signing keys, or bearer tokens to the log.
5. Start the API. Startup applies pending SQL scripts in `src/Infrastructure/Persistence/Sql/Migrations`, checks that the four shop roles exist, and creates the bootstrap Admin only when those settings are present and no active Admin exists.

```powershell
dotnet run --project src/Api/Retail360.Api.csproj --launch-profile http
```

Swagger is at [http://localhost:5117/swagger](http://localhost:5117/swagger).

6. Start the Angular app:

```powershell
cd frontend
npm install
npm start
```

The UI is at [http://localhost:4200](http://localhost:4200). Sign in at [http://localhost:4200/login](http://localhost:4200/login) with the Admin created in step 4. The dev server proxies `/api` to `http://localhost:5117`. The API does not send CORS headers, so a browser on another origin cannot call it directly.

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

The existing Identity tables are kept. Seeded roles are Admin, Manager, Cashier, and Inventory Staff. Sign-in is `POST /api/auth/login` and a short-lived JWT. Five wrong passwords lock that account for five minutes. Role checks use the database on each request. New SQL should use snake_case and set `created_at`, `updated_at`, and `is_deleted` explicitly. There is no EF Core global query filter.

## Current status

Identity tables and the four shop roles are in place. Catalog and Inventory routes require a signed-in user. Admin and Manager can change the catalog. Admin, Manager, and Inventory Staff can change locations and stock. Cashier can read both. The Catalog API covers categories, brands, units, products, activation, deactivation, logical delete when the product has no stock history, and barcode lookup. The Inventory API covers areas, shelves, opening stock, adjustments, transfers, balances, movement history, low stock, and reconciliation. Opening stock, adjustments, and transfers require a client-generated `operationId`. `004_stock_operations.sql` stores that id and a fingerprint of the normalized payload in `stock_operations`. The same operation id and an equivalent payload return the original movements. The same id with a different payload returns HTTP 409. The migration runner applies `002_catalog.sql`, `003_inventory.sql`, `004_stock_operations.sql`, `005_numeric_scale.sql`, `006_suppliers.sql`, and `007_purchases.sql`. Supplier tests run against `retail360_suppliers_test`. Purchase tests run against `retail360_purchasing_test`. A recorded purchase does not change stock. Receiving it posts inbound `purchase` movements through the Inventory ledger. Supplier payments are not implemented. Prices keep the `numeric(12,2)` limits and quantities keep the `numeric(14,3)` limits. Extra fractional digits are rejected. Product has no stock quantity. Authentication tests run with the solution suite against `retail360_identity_test`. This sign-in baseline is not a production identity platform: there is no multi-factor authentication, no external provider, and no permission table. The Angular app has sign-in, Catalog screens for products, categories, brands, and units, Inventory screens for stock, locations, opening stock, adjustments, and transfers, Supplier screens for the supplier list and supplier details, and Purchase screens for the purchase list, recording a purchase, and receiving it. Every shop role can read purchases. Admin and Manager can record or change a recorded purchase. Admin, Manager, and Inventory Staff can receive one. A received purchase cannot be changed on the screen. The purchase list filters by supplier and status. Shelf assignment is collected when the goods are received, and the screen does not post stock itself. Cashier and Inventory Staff can read suppliers. Admin and Manager can change them. The stock list has no product-name query, so the stock screen filters names in the rows it has loaded. The access token from login is kept in `sessionStorage` until `expiresAt`, then removed, and an open counter screen returns to sign-in at that time. Opening stock, adjustments, and transfers can search catalog products by name. The password is not stored. There is no refresh token, and the API returns the token in JSON rather than an HttpOnly cookie, so this browser session is not a production-secure setup. Supplier records are in `006_suppliers.sql`. Admin and Manager can change them. Every shop role can read them. The supplier screens search by name, page with the API limit and offset, and save contact details without changing active status. A supplier has no payment balance. A supplier referenced by a purchase cannot be soft-deleted. See `docs/project/TASKS.md` for the task board.
