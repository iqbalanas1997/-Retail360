# Retail360

Grocery shop management system for a small retail shop. The counter workflow is the priority: find a product, enter a quantity, review the cart, take payment, and show a receipt.

The repository currently contains the foundation: a .NET 8 API, an Angular application, and the PostgreSQL Identity schema. Catalog, inventory, sales, and the other business modules are not implemented yet.

## Stack

- .NET 8 Web API
- Angular 21
- PostgreSQL
- Entity Framework Core

`global.json` pins the .NET SDK to 8.0.412. The Angular app expects Node.js 20.19+, 22.12+, or 24+, and npm 11.21.0 or newer.

## Layout

| Path | Role |
|---|---|
| `src/Domain` | Domain concepts. No EF Core, Identity, or API packages. |
| `src/Application` | Use cases. References Domain only. |
| `src/Infrastructure` | EF Core, PostgreSQL, and ASP.NET Core Identity. |
| `src/Api` | HTTP host and composition root. |
| `frontend` | Angular application. |
| `docs` | Requirements, architecture, and project status. |

Dependency direction: API → Application → Domain, and Infrastructure → Application and Domain.

## Run locally

1. Install the .NET 8 SDK, Node.js, npm 11.21.0 or newer, and PostgreSQL.
2. Create a local database named `retail360`.
3. Set `ConnectionStrings:DefaultConnection` in `src/Api/appsettings.json`. The checked-in value is a local development placeholder.
4. Apply the database migration from the repository root:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Infrastructure/Retail360.Infrastructure.csproj --startup-project src/Api/Retail360.Api.csproj
```

5. Start the API:

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

The UI is at [http://localhost:4200](http://localhost:4200).

## Build

```powershell
dotnet build Retail360.sln
```

```powershell
cd frontend
npm run build
```

## Current status

Identity tables and the four shop roles (Admin, Manager, Cashier, Inventory Staff) are in place. Sign-in, permissions, and business screens are still to be built. See `docs/project/TASKS.md` for the task board.
