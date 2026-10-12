# API Contract Rules

This document starts as a contract policy and becomes a concrete endpoint catalog as APIs are implemented.

## Rules
- REST/JSON.
- Consistent HTTP status codes.
- DTOs are the public contract.
- Pagination/filtering conventions should be standardized before many list endpoints are created.
- Error responses must be consistent.
- Do not expose database entities directly.
- Avoid duplicate endpoints that perform the same use case.

## Errors
Catalog failures return JSON `{ "error": "message" }`.

- `400` invalid input
- `404` missing record, including a deleted record. A product create or update that names a missing unit, category, or brand is 404.
- `409` duplicate name or barcode, or a delete blocked by a product reference

## Lists
`GET /api/products` accepts `name`, `activeOnly`, `limit`, and `offset`. `limit` defaults to 50 and cannot exceed 200. `activeOnly` defaults to false, so inactive products remain visible to catalog management. Deleted products are omitted. Category, brand, and unit lists return every non-deleted row.

## Endpoint catalog
JSON property names are camelCase.

| Method | Path | Behavior |
|---|---|---|
| GET, POST | `/api/categories` | List or create a category. Body: `{ "name": "..." }`. |
| GET, PUT, DELETE | `/api/categories/{id}` | Read, rename, or logically delete a category. `DELETE` sets `is_deleted` and does not run a SQL `DELETE`. |
| GET, POST | `/api/brands` | List or create a brand. |
| GET, PUT, DELETE | `/api/brands/{id}` | Read, rename, or logically delete a brand. |
| GET, POST | `/api/units` | List or create a unit. |
| GET, PUT, DELETE | `/api/units/{id}` | Read, rename, or logically delete a unit. |
| GET | `/api/products` | List non-deleted products. |
| GET | `/api/products/{id}` | Read one non-deleted product, including an inactive product. |
| GET | `/api/products/by-barcode/{barcode}` | Read one active, non-deleted product for an exact barcode. Inactive and deleted products are not returned. |
| POST, PUT | `/api/products` and `/api/products/{id}` | Create or replace catalog fields and the product's current barcode list. Category and brand may be null. Unit is required. |
| POST | `/api/products/{id}/activate` | Set `is_active` to true. |
| POST | `/api/products/{id}/deactivate` | Set `is_active` to false. This does not delete the product. |
| DELETE | `/api/products/{id}` | Logical delete only. Sets `is_deleted` and soft-deletes that product's barcodes. |

The Web API enables OpenAPI in Development at `/swagger`. That UI is host tooling, not a second contract.

## Inventory
Inventory uses the same `{ "error": "message" }` body as Catalog. Invalid input is 400. A missing or soft-deleted area, shelf, or product is 404. A name conflict, a blocked delete, an inbound movement to an inactive shelf, and insufficient stock are 409. There is no update or delete route for a stock movement.

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/areas` | List areas that are not soft-deleted. |
| GET | `/api/areas/{id}` | Read one area. |
| POST | `/api/areas` | Create an area from `{ "name" }`. Returns 201. |
| PUT | `/api/areas/{id}` | Rename from `{ "name" }`. |
| POST | `/api/areas/{id}/activate` | Set `isActive` to true. |
| POST | `/api/areas/{id}/deactivate` | Set `isActive` to false. |
| DELETE | `/api/areas/{id}` | Logical delete. Returns 204. Rejected while a non-deleted shelf, a balance, or a movement exists. |
| GET | `/api/shelves?areaId=` | List shelves. `areaId` is optional. |
| GET | `/api/shelves/{id}` | Read one shelf, including its area name. |
| POST | `/api/shelves` | Create a shelf from `{ "areaId", "name" }`. Returns 201. |
| PUT | `/api/shelves/{id}` | Rename from `{ "name" }`. |
| POST | `/api/shelves/{id}/activate` | Set `isActive` to true. |
| POST | `/api/shelves/{id}/deactivate` | Set `isActive` to false. Existing stock stays. |
| DELETE | `/api/shelves/{id}` | Logical delete. Returns 204. Rejected when a balance or movement references the shelf. |
| POST | `/api/inventory/opening` | `{ "operationId", "productId", "shelfId", "quantity", "reason" }`. `operationId` is a client-supplied UUID and is required. Quantity is strictly positive. Writes one inbound opening movement and updates the shelf balance. |
| POST | `/api/inventory/adjustments` | `{ "operationId", "productId", "shelfId", "quantity", "reason" }`. `operationId` is required. Quantity is signed and must not be zero. A positive value increases stock. A negative value decreases it. The stored movement quantity is the absolute value with direction `in` or `out`. |
| POST | `/api/inventory/transfers` | `{ "operationId", "productId", "sourceShelfId", "destinationShelfId", "quantity", "reason" }`. `operationId` is required. Quantity is strictly positive. Reason may be empty. Writes the outbound and inbound rows in one transaction. |
| GET | `/api/inventory/balances?productId=&shelfId=` | Current shelf balances. Both filters are optional. |
| GET | `/api/inventory/movements?productId=&shelfId=&limit=&offset=` | Movement history. Limit defaults to 50 and cannot exceed 200. |
| GET | `/api/inventory/stock?status=&limit=&offset=` | Shop-wide on-hand quantity. `status` is `all`, `low`, or `out`. |
| GET | `/api/inventory/reconciliation` | Rows whose stored balance does not equal inbound minus outbound history. An empty array means they match. |

Repeating a stock request with the same `operationId` and the same payload returns the original movements and does not change stock again. The same `operationId` with a different payload is 409. A missing `operationId` is 400. Creating a shelf for a missing or soft-deleted area is 404.

Opening and adjustment reasons are required and are at most 200 characters. `damaged`, `expired`, and `missing` are standard reasons. Other correction text is allowed. A soft-deleted product is rejected. An inactive product may still be adjusted. An inactive shelf rejects opening stock, a positive adjustment, and a transfer in. It allows a negative adjustment and a transfer out when the shelf still has enough stock. Reading an inactive shelf balance is allowed. Stock on an inactive shelf is included in the shop total. Out of stock means that total is zero. Low stock means the total is greater than zero and less than or equal to Catalog `minimum_stock`. A product with no balance rows is on hand zero. Purchasing and Sales endpoints are not part of this contract.
