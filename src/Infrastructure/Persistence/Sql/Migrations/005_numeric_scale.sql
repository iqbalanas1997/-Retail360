-- numeric(12,2) and numeric(14,3) round extra fractional digits before a check
-- constraint can see them. These columns become unconstrained numeric so the
-- original value is checked. Accepted values stay inside the old limits:
-- prices match numeric(12,2), and quantities match numeric(14,3).
-- Existing rows already fit, so the new checks succeed.
-- Rollback: drop ck_products_purchase_price_scale, ck_products_selling_price_scale,
-- ck_products_minimum_stock_scale, ck_stock_balances_quantity_scale, and
-- ck_stock_movements_quantity_scale, then alter the columns back to
-- numeric(12,2) or numeric(14,3). That cast is exact for values these checks
-- accept, and it restores silent rounding.

alter table products
    alter column purchase_price type numeric,
    alter column selling_price type numeric,
    alter column minimum_stock type numeric;

alter table products
    add constraint ck_products_purchase_price_scale check (
        purchase_price = round(purchase_price, 2)
        and purchase_price <= 9999999999.99
    ),
    add constraint ck_products_selling_price_scale check (
        selling_price = round(selling_price, 2)
        and selling_price <= 9999999999.99
    ),
    add constraint ck_products_minimum_stock_scale check (
        minimum_stock = round(minimum_stock, 3)
        and minimum_stock <= 99999999999.999
    );

alter table stock_balances
    alter column quantity type numeric;

alter table stock_balances
    add constraint ck_stock_balances_quantity_scale check (
        quantity = round(quantity, 3)
        and quantity <= 99999999999.999
    );

alter table stock_movements
    alter column quantity type numeric;

alter table stock_movements
    add constraint ck_stock_movements_quantity_scale check (
        quantity = round(quantity, 3)
        and quantity <= 99999999999.999
    );
