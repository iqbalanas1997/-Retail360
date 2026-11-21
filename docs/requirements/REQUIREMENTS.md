# Grocery Shop Management System — High-Level Requirements

## Purpose
Digitize daily grocery shop operations and provide real-time visibility into stock, sales, purchases, expenses, suppliers, customers and business performance.

## Modules

### 1. Product & Inventory Management
- Products, categories, brands and units.
- Purchase price, selling price and minimum stock.
- Current stock and stock movement. On-hand quantity is per shelf, not a column on the product.
- Stock by Area -> Shelf. An area contains shelves. The shop is one shop.
- Multiple shelves per product.
- Transfer stock between shelves without changing the shop total.
- Opening stock is a stock movement with a reason, not a purchase.
- Damaged, expired, and missing goods are adjustment reasons.
- Low-stock and out-of-stock identification from the sum of shelf balances compared with minimum stock. A shelf quantity is never negative.

### 2. Purchase Management
- Supplier purchases.
- Purchase invoices and product quantities.
- Inventory update after purchase.
- Supplier-wise purchase history.

### 3. Sales / POS
- Sell by barcode or search.
- Sales invoice/receipt.
- Cash and credit sales.
- Automatic stock reduction.
- Sales history.

### 4. Supplier Management
- Supplier information.
- Supplier-wise purchase history.
- Supplier payments and outstanding balances.

### 5. Customer Management
- Customer information.
- Customer-wise sales history.
- Credit sales and outstanding balances.

The customer record stores customer information. A cash sale can name a customer without creating a balance. A credit sale posts one charge, and a later payment is allocated to the oldest open charges. The outstanding balance is derived from that ledger. It is not stored on the customer. Supplier payments remain future work.

### 6. Expenses
- Daily shop expenses.
- Expense categories.
- Daily/monthly expense summaries.

### 7. Dashboard & Reports
- Daily/monthly sales.
- Purchases.
- Expenses.
- Profit.
- Current inventory.
- Inventory value.
- Low/out-of-stock products.
- Supplier payable.
- Customer receivable.
- Product-wise sales.

### 8. Users & Access
- Users and roles.
- Admin, Manager, Cashier, Inventory Staff.
- Role-specific permissions.
- Important activity/audit log.

### 9. Barcode
Barcode is a Catalog capability, not a separate module. Values are stored on the product in `product_barcodes`.
- Scan existing product barcodes.
- Fast product identification at POS/inventory.
- Custom/internal barcodes.

### 10. Future Scalability
Design for future additions without implementing them prematurely:
- Multiple branches.
- Advanced accounting.
- Online orders.
- Customer loyalty/points.
- Mobile application.
- Advanced expiry/batch management.

## MVP priority
The first implementation should prioritize:
1. Product/catalog
2. Inventory and stock movements
3. Barcode lookup through Catalog, and product search
4. POS sales
5. Purchases
6. Customers/suppliers
7. Expenses
8. Basic dashboard/reports
9. Users/roles/audit

Future scalability items are architectural considerations, not automatic MVP features.

## Verification status
On 2026-11-08 the implemented catalog, inventory, purchasing, cash sales, suppliers, customers, expenses, dashboard, users, and audit behavior was regression-tested. The automated suites passed, and the same workflows were exercised on the local development database and in the browser. No defect was found. Credit sales, supplier payments, profit, inventory value, payables, and receivables are still not implemented.
