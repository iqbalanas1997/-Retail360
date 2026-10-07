# Domain Model

This is the initial domain map. Detailed fields and relationships must be finalized before implementation of each module.

## Core concepts

### Product
A sellable/stocked item. Related to category, brand, unit, barcode(s), pricing and inventory locations.

### Category
Groups products.

### Brand
Product brand.

### Unit
Defines how a product quantity is measured/sold.

### Location / Area / Shelf
Represents where inventory is physically stored. A product can exist in multiple shelves/locations.

### Stock Movement
Auditable movement of stock caused by purchase, sale, transfer, adjustment, damage, expiry or other approved reason.

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

## Important invariants
- A sale cannot reduce stock below allowed policy.
- A purchase increases inventory according to received quantities.
- Transfers move stock between locations without changing total stock.
- Adjustments must have a reason.
- Monetary calculations must use decimal types.
- Historical transactions should not be silently rewritten.
