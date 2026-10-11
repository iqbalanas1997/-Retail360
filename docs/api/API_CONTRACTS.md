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
- `404` missing record, including a deleted record
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
The Inventory behavior below is approved and is not implemented. Routes, request bodies, response bodies, and status codes are not finalized. Do not treat this section as a callable contract.

When an Inventory API is designed, it must be able to:

- Maintain areas and shelves, including activation and deactivation.
- Record opening stock for a product on a shelf with a positive quantity and a required reason.
- Record an adjustment with a required reason. Damaged, expired, and missing are reasons, not separate operations.
- Transfer a positive quantity from one shelf to another without changing the shop total.
- Read shelf balances, movement history, and shop-wide on-hand quantity.
- Identify out-of-stock products and low-stock products from the shop-wide sum compared with Catalog `minimum_stock`.

The same rules apply whatever routes are chosen later. A movement is not edited or deleted through the API. A correction is a new movement. A soft-deleted product is rejected. An inactive product may still be counted or adjusted. An inactive shelf must not receive a new stock movement. A location with a balance or movement history is not soft-deleted. A request that would make a shelf balance negative is rejected, including when two requests run together. Purchasing and Sales endpoints are not part of this contract.
