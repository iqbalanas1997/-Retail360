# Module Catalog

| Module | Purpose | Main concepts | Status |
|---|---|---|---|
| Catalog | Product master data | Product, Category, Brand, Unit, ProductBarcode, current prices, minimum-stock threshold | Backend implemented. Angular screens cover list, create, edit, barcodes, and category, brand, and unit names. Enter in the barcode field adds that code and does not save the product. Saving sends the codes already on the form. A saved barcode can be printed as a Code 128 label. Generate shop barcode proposes an `R360-` code and does not store it until save succeeds. That proposal is not checked against other products until save. |
| Inventory | On-hand stock and locations | Area, Shelf, stock balance per product and shelf, append-only movement | Backend implemented in `003_inventory.sql`. No stock column on Product. Angular screens cover stock, locations, opening stock, adjustments, and transfers. |
| Sales | Cash sales and sales history | Sale, SaleLine, receipt number, optional customer | Backend implemented in `008_sales.sql`. Angular POS, receipt, and sales history. Cash only. An optional customer does not change the payment. No credit, return, or discount. |
| Purchasing | Supplier purchases | Purchase, PurchaseLine | Backend implemented in `007_purchases.sql`. Angular list, record, and receive screens. No payment, return, or cancellation. |
| Suppliers | Supplier records | Supplier | Backend implemented in `006_suppliers.sql`. Angular screens cover list, create, edit, activation, soft delete, and that supplier's purchase history. The history uses the existing purchase list. No payment or payable balance. |
| Customers | Customer records | Customer | Backend implemented in `013_customers.sql`. Angular list and form are `/customers` for Admin and Manager. A cash sale may name one active customer. Receivables, Udhaar, and customer payments are not implemented. |
| Expenses | Shop expenses | Expense, fixed category | Backend implemented in `009_expenses.sql` and `010_expense_amount_scale.sql`. Categories are a fixed list, not a managed catalog. Angular list, record, and edit screens. Cash only. An expense does not change stock, purchases, suppliers, or sales. |
| Reporting | Dashboard/reports | Read-only sales, expense, purchase, and low-stock summaries | Backend and Angular dashboard. No stored report rows. No profit, inventory value, payable, or receivable figure. |
| Identity | Users/roles | User, Role | Backend sign-in is in place on the existing Identity tables. JWT access tokens and role checks are implemented. Permission is not a table. Angular sign-in calls `POST /api/auth/login` and keeps the access token in `sessionStorage` until expiry or a 401. `/account` changes the signed-in user's password and then returns to sign-in. Admin manages staff at `/users`: list, create, one existing role, edit, activate, deactivate, and soft delete. A new password on that form signs that account out. |
| Audit | Important activity log | AuditEntry | Backend implemented in `012_audit_log.sql`. Angular history is `/audit` for Admin. Append-only. No edit or delete route. |

## Inventory scope
Inventory depends on Catalog for product identity, the product's unit, and `minimum_stock`. It does not own prices, barcodes, categories, or brands. Barcode stays a Catalog capability. There is no separate Barcode module.

A product that has a stock balance or a stock movement is not soft-deleted. The implemented Inventory scope is areas, shelves, opening stock, adjustments, and transfers. Damaged, expired, and missing goods are adjustment reasons. Shop-wide on-hand quantity is the sum of shelf balances, including inactive shelves. Low stock compares that sum with `minimum_stock`. An inactive shelf rejects inbound stock and allows outbound stock.

Inventory does not include Purchasing, Sales, suppliers, branches, batch or expiry dates, or a stock quantity on `products`. Purchasing and Sales post through this ledger. Movement rows are not edited or soft-deleted.

## Sales scope
A sale is one completed cash document. The server copies the catalog selling price and product name onto each line and calculates the totals. The caller names the shelf the goods leave. Admin, Manager, and Cashier can complete a sale. Every shop role can read sales. Inventory Staff cannot sell. The Angular counter is `/pos`. It searches by name, looks up a barcode, and sends the operation id, product, shelf, and quantity. An optional customer search can attach one active customer. The sale can be completed with no customer. A newer search replaces an older response. A barcode scan still adds its line if that response arrives after a newer search, and a repeated scan does not add another line while that lookup is still running. The scan field is cleared when the lookup starts so the next scan is not appended. A failed shelf load can be retried. The same operation id is kept until the receipt opens. `/sales` lists receipts and opens the API receipt.

## Suppliers scope
A supplier is the shop's record of a business or person it can buy from. The record is the supplier id, name, optional contact details, and `is_active`. Names are unique among non-deleted rows, compared case-insensitively. Admin and Manager can change suppliers. Every shop role can read them. The Angular screens use those same routes. `/suppliers/:id` lists that supplier's non-deleted purchases through `GET /api/purchases?supplierId=`, with the same page size as the purchase list. Recorded and received documents stay separate. Every shop role can read that history. Supplier payment is a separate concern and is not implemented. Purchasing references the supplier id and is implemented separately.

## Customers scope
A customer is the shop's record of a person it can sell to. The record is the customer id, name, optional phone, email, address, and notes, and `is_active`. Names are not unique. Admin and Manager can change customers. Admin, Manager, and Cashier can read them. Inventory Staff cannot. Cashier reads omit address and notes. `/customers/:id` lists that customer's cash sales through `GET /api/customers/{id}/sales`. Each row shows the receipt, time, cash payment, and sale total. Those totals are not an amount owed. A cash sale may leave the customer empty. A new sale can name only an active customer. Deactivation and soft delete do not remove the customer from a sale already recorded. Credit sales and customer payments are not part of this module.

## Reporting scope
Reporting reads completed sales, expenses, purchases, and current stock. It does not own those records and does not post stock. Admin and Manager can open `/dashboard`. Cashier and Inventory Staff cannot. The selected range is inclusive. Sale times and received purchases use the UTC calendar date. Expense dates and purchase document dates are the dates stored on those records. Recorded purchase value and received purchase value are separate. Low stock follows the existing shop-wide balance rule for active products only. Profit is not shown because a sale does not store the cost of the goods sold.

## Expenses scope
Expenses owns a shop expense record. It does not own inventory, purchases, supplier balances, sales, or customer receivables. The category is one of `rent`, `utilities`, `wages`, `transport`, `supplies`, `repairs`, or `other`. There is no category-management screen. The payment method is `cash`. Amount uses `numeric(12,2)` and must be greater than zero. The expense date is required. Notes are optional. Admin and Manager can record, change, and soft-delete an expense. Every shop role can read the list. The list total is the sum of the expenses that match the current date and category filter, including rows beyond the current page.

## Identity scope
Identity owns sign-in and staff accounts. The roles stay Admin, Manager, Cashier, and Inventory Staff. There is no permission table and no second user table. Admin is the only role that can manage users. `is_active` stops sign-in without deleting the row. `lockout_end` remains the five-minute password lock. `security_stamp` is copied into the access token and checked on each request. Saving a password replaces it. Login attempts are also limited in memory for the current process. A removed user name stays reserved because the unique user-name index includes soft-deleted rows.

## Audit scope
Audit records a successful business change. It does not own the user, product, stock, purchase, sale, expense, or supplier record. The entry is written in the same database transaction as that change. A failed change rolls the entry back. Repeating the same operation id and payload does not write a second entry. Sign-in, bootstrap Admin creation, and a user-name edit are not entries. A saved password is `user.password_changed` and stores the user name only. A failed password check is not an entry. A password, password hash, access token, and signing key are never stored. Supplier phone, email, address, and notes are not stored. Customer phone, email, address, and notes are not stored. Expense notes are not stored. Purchase and sale stock posts are not a second stock entry; the purchase or sale entry is the record. Product activation is a product edit. Admin is the only role that can read the log. Entries stay after the business row is soft-deleted.

## Duplication prevention
Before adding a new module or concept:
1. Search this catalog.
2. Search `DOMAIN_MODEL.md`.
3. Search the source code.
4. If an equivalent exists, extend it rather than creating a parallel concept.
