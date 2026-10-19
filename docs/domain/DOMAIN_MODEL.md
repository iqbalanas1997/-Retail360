# Domain Model

This is the domain map. The Catalog, Inventory, Suppliers, Purchasing, and Sales sections below match the implemented backend. Other modules still need their fields finalized before implementation.

## Core concepts

### Product
A sellable item defined by Catalog. It has a name, one required unit, an optional category, an optional brand, current purchase price and selling price within the `numeric(12,2)` limits, minimum stock within the `numeric(14,3)` limits, and `is_active`. It has no SKU and no stock quantity. Identity for the MVP is the product id, name, and barcode values.

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
A storage place in one area. Stock is held here, not on the product and not on the area. A shelf has id, area, name, `is_active`, created_at, updated_at, and is_deleted. A product may have stock on many shelves. An inactive shelf rejects inbound stock, including opening stock, a positive adjustment, and a transfer destination. It allows an outbound adjustment or a transfer source when the remaining quantity stays at or above zero. Its existing balance and history stay and can still be read. A shelf or area with a balance or any movement history is not soft-deleted.

### Stock balance
The current on-hand quantity of one product on one shelf. Quantity uses `decimal`, stays within `99999999999.999`, has at most 3 fractional digits, and cannot be negative. There is at most one balance per product and shelf. The balance is updated only in the same transaction as the movement that explains the change. It is not a column on Product. `minimum_stock` remains the Catalog threshold.

### Stock movement
An append-only explanation of a stock change. Quantity is a positive `decimal` with at most 3 fractional digits. Direction is inbound or outbound. The movement types are opening, adjustment, transfer, purchase, and sale. Damaged, expired, and missing are adjustment reasons, not types. A movement is not edited and is not soft-deleted. A correction is a new compensating movement with a reason.

Opening stock records the product, shelf, positive quantity, time, and a required reason. It is not a purchase. An adjustment requires a reason. A transfer records the same positive quantity out of the source shelf and into the destination shelf. The shop total does not change.

A product with a stock balance row or any stock movement cannot be soft-deleted, even when the quantity is zero. A product with no Inventory history can be soft-deleted. A soft-deleted product cannot receive a new movement. An inactive product can still be counted, adjusted, or purchased. It cannot be sold, because `is_active` means the product is offered. A purchase receipt is an inbound `purchase` movement. Its document contract is ADR-014. A cash sale is an outbound `sale` movement. Its document contract is ADR-015.

### Purchase
A supplier invoice for one or more products. It stores the supplier id, purchase date, optional reference, optional notes, status, and the server-calculated total. Each line stores the product id, quantity, unit cost, and line total. `recorded` does not change stock. `received` means every current line was posted to a shelf as an inbound `purchase` movement. A received purchase is not edited or soft-deleted. Payments, returns, and cancellation are not part of this document.

### Sale
A completed cash sale for one or more products. It stores the receipt number, sold time, payment method `cash`, and the server-calculated total. Each line stores the product id, the shelf the goods left, the product name and unit selling price copied at sale time, the quantity, and the line total. Completing the sale posts one outbound `sale` movement per line through the Inventory ledger. The caller names the shelf. Credit, returns, refunds, discounts, and cancellation are not part of this document.

### Supplier
A business or person the shop can buy from. Fields are id, name, optional contact person, phone, email, address, and notes, plus `is_active`, created_at, updated_at, and is_deleted. The name is required and unique among non-deleted suppliers, compared case-insensitively. A soft-deleted name can be used again. An inactive supplier can still be edited. Deactivation does not delete the supplier. Supplier payments and payable balances are not part of this record. A purchase references the supplier id. A supplier referenced by a purchase cannot be soft-deleted.

### Customer
Buyer with optional credit/receivable history.

### Expense
Business expense categorized and dated.

### User / Role / Permission
Controls system access.

Domain keeps the role names in `ShopRoleNames`: Admin, Manager, Cashier, and Inventory Staff. The user and role rows live in the existing PostgreSQL Identity tables and are read with Dapper from Infrastructure. Users and roles carry `created_at`, `updated_at`, and `is_deleted`. There is no separate inactive flag. `lockout_end` blocks sign-in while it is in the future. Permission is not a table. Sign-in is the JWT baseline in ADR-012. Passwords are stored only as hashes.

### Audit Entry
Records important actions where accountability is required.

## Catalog boundaries
Catalog owns product identity, barcode values, the current price, and the minimum-stock threshold. Category and brand are optional. Unit is required. Inventory owns quantities, stock movements, and shelf locations. A later sale line or purchase line copies the price used on that document. Changing the product's current price does not rewrite those documents.

`is_active` and `is_deleted` are different. Deactivation does not delete the product. Normal catalog reads exclude `is_deleted` rows. Barcode lookup also requires `is_active`.

## Inventory boundaries
Inventory owns areas, shelves, shelf balances, and the movement ledger. Catalog owns product identity, the unit, barcodes, current prices, and `minimum_stock`. Shop-wide on-hand quantity is the sum of that product's shelf balances, including quantity left on a deactivated shelf. Out of stock means that sum is zero. Low stock means that sum is greater than zero and less than or equal to `minimum_stock`.

A stock operation locks the product row, records the client operation key, then locks the affected shelf rows and balance rows in ascending shelf id order, writes the movements, and updates the balances in one transaction. The same key and payload is the same operation. A different payload for that key is rejected. It rejects a product that is already soft-deleted when a new key acquires that product lock. Product soft-delete uses that same product lock and refuses the delete when a balance or movement is visible under the lock. Shelf soft-delete locks the shelf and rejects the delete when the locked history check finds a balance or movement. Area soft-delete locks the area and its shelves, then rejects the delete when a non-deleted shelf, balance, or movement remains. Any failure rolls the transaction back. No isolation level is required by this model. A balance list still shows stock if the area row is soft-deleted.

## Important invariants
- On-hand quantity at a shelf is never negative. There is no override.
- A purchase increases inventory only when its goods are received, and only by those received quantities.
- A cash sale decreases inventory by the quantities on its lines, and only when the sale commits.
- Transfers move stock between shelves without changing total shop stock.
- Opening stock and adjustments require a reason. Damaged, expired, and missing are adjustment reasons.
- Movement and balance quantities use `decimal` and stay within the `numeric(14,3)` limits. Do not use floating point.
- Stock movements are not edited, soft-deleted, or rewritten. Corrections are new movements.
