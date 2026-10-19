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

## Authentication
`POST /api/auth/login` is anonymous. Body: `{ "userName", "password" }`. A success returns `{ "accessToken", "tokenType", "expiresAt", "userId", "userName" }` and does not include a password hash. `tokenType` is `Bearer`. Send the token as `Authorization: Bearer <accessToken>`.

Unknown user names, wrong passwords, soft-deleted users, and users whose `lockout_end` is still in the future all return 401 `{ "error": "The user name or password is incorrect." }`. Five wrong passwords for an existing unlocked user set that lockout for five minutes. A successful login clears the failed count only while the account is still unlocked. A missing body is 400. A missing or invalid token on any other route is 401 `{ "error": "Authentication is required." }`. A signed-in role that is not allowed is 403 `{ "error": "You do not have access to this action." }`. `POST /api/auth/login` is the only anonymous business route.

The token is checked for signature, issuer, audience, and expiry. Roles are read from `asp_net_user_roles` for the user id in the token. A role claim that is not assigned in the database does not grant access. There is no public registration endpoint.

### Access matrix
Assumption: every shop role may look up products and stock. Only Admin and Manager may change catalog data, including prices, activation, and soft delete. Cashier must not post stock or edit locations. Inventory Staff may maintain locations and stock and may read the catalog, but may not change prices.

| Action | Admin | Manager | Cashier | Inventory Staff |
|---|---|---|---|---|
| Catalog read | yes | yes | yes | yes |
| Catalog create, update, activate, deactivate, delete | yes | yes | no | no |
| Inventory read, including balances, movements, stock, and reconciliation | yes | yes | yes | yes |
| Areas, shelves, opening stock, adjustments, transfers | yes | yes | no | yes |
| Supplier read | yes | yes | yes | yes |
| Supplier create, update, activate, deactivate, delete | yes | yes | no | no |

## Errors
Catalog failures return JSON `{ "error": "message" }`. A signed-in caller still receives these bodies after authorization succeeds.

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
| DELETE | `/api/products/{id}` | Logical delete only. Sets `is_deleted` and soft-deletes that product's barcodes. HTTP 409 when the product has a stock balance row or a stock movement, including a zero balance. |

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

Prices are from 0 through `9999999999.99` with at most 2 fractional digits. Minimum stock and stock quantities have at most 3 fractional digits and are at most `99999999999.999`. A stock quantity must be greater than zero. An extra digit is rejected. A shelf total above the maximum is HTTP 409, and that operation is not stored.

Opening and adjustment reasons are required and are at most 200 characters. `damaged`, `expired`, and `missing` are standard reasons. Other correction text is allowed. A soft-deleted product is rejected. An inactive product may still be adjusted. An inactive shelf rejects opening stock, a positive adjustment, and a transfer in. It allows a negative adjustment and a transfer out when the shelf still has enough stock. Reading an inactive shelf balance is allowed. Stock on an inactive shelf is included in the shop total. Out of stock means that total is zero. Low stock means the total is greater than zero and less than or equal to Catalog `minimum_stock`. A product with no balance rows is on hand zero. Purchasing and Sales endpoints are not part of this contract.

## Suppliers
Supplier failures use the same `{ "error": "message" }` body. Invalid input is 400. A missing or soft-deleted supplier is 404. A case-insensitive duplicate name is 409. `DELETE` sets `is_deleted` and does not run a SQL `DELETE`.

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/suppliers?name=&activeOnly=&limit=&offset=` | List suppliers that are not soft-deleted. `name` matches part of the supplier name. `activeOnly` defaults to false. `limit` defaults to 50 and cannot exceed 200. |
| GET | `/api/suppliers/{id}` | Read one supplier, including an inactive supplier. |
| POST | `/api/suppliers` | Create from `{ "name", "contactPerson", "phone", "email", "address", "notes" }`. Optional fields may be null or blank. Returns 201. The supplier starts active. |
| PUT | `/api/suppliers/{id}` | Replace those same fields. An inactive supplier can be updated. This does not change `isActive`. |
| POST | `/api/suppliers/{id}/activate` | Set `isActive` to true. |
| POST | `/api/suppliers/{id}/deactivate` | Set `isActive` to false. This does not delete the supplier. |
| DELETE | `/api/suppliers/{id}` | Logical delete. Returns 204. The name can be used by a later supplier. |

Name is required and at most 200 characters. Contact person is at most 200. Phone is at most 40. Email, when present, is one address of at most 200 characters. Address is at most 400. Notes are at most 1000. There is no supplier payment endpoint. A supplier referenced by a purchase cannot be soft-deleted.

## Purchasing
Purchase failures use the same `{ "error": "message" }` body. Invalid input is 400. A missing supplier, product, shelf, or purchase is 404. A conflict is 409. `DELETE` sets `is_deleted` and does not run a SQL `DELETE`.

`PurchaseRead` allows every shop role. `PurchaseRecord` allows Admin and Manager. `PurchaseReceive` allows Admin, Manager, and Inventory Staff.

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/purchases?supplierId=&status=&limit=&offset=` | List purchases that are not soft-deleted. `status` is `recorded` or `received`. `limit` defaults to 50 and cannot exceed 200. |
| GET | `/api/purchases/{id}` | Read one purchase and its current lines. |
| POST | `/api/purchases` | Create a recorded purchase from `{ "supplierId", "purchaseDate", "reference", "notes", "lines": [{ "productId", "quantity", "unitCost" }] }`. The server calculates totals. Returns 201. |
| PUT | `/api/purchases/{id}` | Replace a recorded purchase. A received purchase is 409. |
| DELETE | `/api/purchases/{id}` | Soft-delete a recorded purchase. Returns 204. A received purchase is 409. |
| POST | `/api/purchases/{id}/receive` | Receive every current line from `{ "operationId", "lines": [{ "lineId", "shelfId" }] }`. Posts inbound `purchase` movements and sets the purchase to `received`. |

The supplier must be active when the purchase is recorded or replaced. An inactive product is allowed. A soft-deleted product is 404. Quantity is greater than zero with at most 3 decimal places. Unit cost is zero or greater with at most 2 decimal places. Line total is `round(quantity * unitCost, 2)`, half away from zero. An inactive shelf cannot receive stock, and that failure rolls the receive back. The same `operationId` and the same shelf assignment return the received purchase. A different assignment or a second receive is 409. There is no payment, return, or cancellation route.

## Sales
Sale failures use the same `{ "error": "message" }` body. Invalid input is 400. A missing product, shelf, or sale is 404. A conflict is 409.

`SaleRead` allows every shop role. `SaleComplete` allows Admin, Manager, and Cashier. Inventory Staff can read sales and cannot complete one.

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/sales?receiptNumber=&limit=&offset=` | List completed sales, newest first. `receiptNumber` is an exact match. `limit` defaults to 50 and cannot exceed 200. |
| GET | `/api/sales/{id}` | Read one sale and its lines, including the snapshotted product name and unit price and the current shelf name. |
| POST | `/api/sales` | Complete a cash sale from `{ "operationId", "lines": [{ "productId", "shelfId", "quantity" }] }`. The server copies the catalog selling price and calculates totals. Returns 201. |

The product must be active and not soft-deleted. Quantity is greater than zero with at most 3 decimal places. The client does not send a price or a total. Line total is `round(quantity * sellingPrice, 2)`, half away from zero. The caller names the shelf. An inactive shelf can be sold from. A shelf that does not have enough stock is 409, and the sale is not stored. The same `operationId` and the same lines return the original sale. A different payload is 409. There is no credit, discount, return, refund, or cancellation route.
