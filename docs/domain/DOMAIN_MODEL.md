# Domain Model

This is the domain map. The Catalog and Inventory sections below are the approved models. Inventory is not implemented. Other modules still need their fields finalized before implementation.

## Core concepts

### Product
A sellable item defined by Catalog. It has a name, one required unit, an optional category, an optional brand, current purchase price `numeric(12,2)`, current selling price `numeric(12,2)`, minimum stock `numeric(14,3)`, and `is_active`. It has no SKU and no stock quantity. Identity for the MVP is the product id, name, and barcode values.

### Category
A flat product group. No parent category in the MVP.

### Brand
The product's brand. Optional. Fields are id, name, created_at, updated_at, and is_deleted.

### Unit
How one product's quantity is measured or sold. A product has exactly one unit. There is no alternate unit and no conversion factor in the MVP.

### Barcode
A Catalog scan code in `product_barcodes`. One product may have several barcodes, or none. One non-deleted barcode value identifies one product. POS lookup goes through Catalog. There is no separate Barcode module.

### Area
A named part of the single shop. An area contains shelves. There is no branch. Fields are id, name, `is_active`, created_at, updated_at, and is_deleted. Deactivation is not deletion.

### Shelf
A storage place in one area. Stock is held here, not on the product and not on the area. A shelf has id, area, name, `is_active`, created_at, updated_at, and is_deleted. A product may have stock on many shelves. An inactive shelf must not receive a new stock movement. Its existing balance and history stay. A shelf or area with a balance or any movement history is not soft-deleted.

### Stock balance
The current on-hand quantity of one product on one shelf. Quantity is `numeric(14,3)` and cannot be negative. There is at most one balance per product and shelf. The balance is updated only in the same transaction as the movement that explains the change. It is not a column on Product. `minimum_stock` remains the Catalog threshold.

### Stock movement
An append-only explanation of a stock change. Quantity is a positive `numeric(14,3)`. Direction is inbound or outbound. The initial types are opening, adjustment, and transfer. Damaged, expired, and missing are adjustment reasons, not types. A movement is not edited and is not soft-deleted. A correction is a new compensating movement with a reason.

Opening stock records the product, shelf, positive quantity, time, and a required reason. It is not a purchase. An adjustment requires a reason. A transfer records the same positive quantity out of the source shelf and into the destination shelf. The shop total does not change.

A soft-deleted product cannot receive a new movement. An inactive product can still be counted or adjusted. Purchasing and Sales may later cause movements. Their document contracts are not defined yet.

### Purchase
Supplier-side acquisition of products and quantities.

### Sale
Customer-side transaction containing sale lines, quantities, prices and payment information.

### Supplier
Business/person supplying products, with purchase and payable history.

### Customer
Buyer with optional credit/receivable history.

### Expense
Business expense categorized and dated.

### User / Role / Permission
Controls system access.

Domain keeps the role names in `ShopRoleNames`: Admin, Manager, Cashier, and Inventory Staff. The user and role rows live in the existing PostgreSQL Identity tables and are read with Dapper from Infrastructure. Users and roles carry `created_at`, `updated_at`, and `is_deleted`. Permission is not a table yet. Sign-in is not implemented.

### Audit Entry
Records important actions where accountability is required.

## Catalog boundaries
Catalog owns product identity, barcode values, the current price, and the minimum-stock threshold. Category and brand are optional. Unit is required. Inventory owns quantities, stock movements, and shelf locations. A later sale line or purchase line copies the price used on that document. Changing the product's current price does not rewrite those documents.

`is_active` and `is_deleted` are different. Deactivation does not delete the product. Normal catalog reads exclude `is_deleted` rows. Barcode lookup also requires `is_active`.

## Inventory boundaries
Inventory owns areas, shelves, shelf balances, and the movement ledger. Catalog owns product identity, the unit, barcodes, current prices, and `minimum_stock`. Shop-wide on-hand quantity is the sum of that product's shelf balances, including quantity left on a deactivated shelf. Out of stock means that sum is zero. Low stock means that sum is greater than zero and less than or equal to `minimum_stock`.

A stock operation locks the affected balance rows in ascending shelf id order, writes the movements, and updates the balances in one transaction. Any failure rolls the transaction back. No isolation level is required by this model.

## Important invariants
- On-hand quantity at a shelf is never negative. There is no override.
- A purchase increases inventory according to received quantities. Purchasing is not implemented.
- Transfers move stock between shelves without changing total shop stock.
- Opening stock and adjustments require a reason. Damaged, expired, and missing are adjustment reasons.
- Movement and balance quantities use `decimal` and `numeric(14,3)`. Do not use floating point.
- Stock movements are not edited, soft-deleted, or rewritten. Corrections are new movements.
