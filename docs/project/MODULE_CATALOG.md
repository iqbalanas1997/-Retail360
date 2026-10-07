# Module Catalog

| Module | Purpose | Main concepts | Status |
|---|---|---|---|
| Catalog | Product master data | Product, Category, Brand, Unit, Barcode | Planned |
| Inventory | Stock and locations | Stock, Shelf, Movement, Adjustment | Planned |
| Sales | POS and sales history | Sale, SaleLine, Payment | Planned |
| Purchasing | Supplier purchases | Purchase, PurchaseLine | Planned |
| Suppliers | Supplier records/payables | Supplier, SupplierPayment | Planned |
| Customers | Customer records/receivables | Customer, CustomerPayment | Planned |
| Expenses | Shop expenses | Expense, ExpenseCategory | Planned |
| Reporting | Dashboard/reports | Sales/Purchase/Profit/Stock projections | Planned |
| Identity | Users/roles | User, Role, Permission | In progress. Identity tables, role seed, and Infrastructure Identity types exist. Domain keeps role names only. Permission is not modeled. |
| Audit | Important activity log | AuditEntry | Planned |
| Barcode | Barcode resolution | Barcode mapping | Planned |

## Duplication prevention
Before adding a new module or concept:
1. Search this catalog.
2. Search `DOMAIN_MODEL.md`.
3. Search the source code.
4. If an equivalent exists, extend it rather than creating a parallel concept.
