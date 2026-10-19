-- Cash sales and the outbound sale stock-movement type.
-- Do not edit earlier scripts. Purchase movements stay inbound.
-- Rollback: drop sale_lines, sales, and sale_receipt_counter, then restore the
-- stock_movements checks from 007_purchases.sql and drop ck_stock_movements_sale_direction.

alter table stock_movements drop constraint ck_stock_movements_type;
alter table stock_movements drop constraint ck_stock_movements_reason;
alter table stock_movements drop constraint ck_stock_movements_purchase_direction;

alter table stock_movements
    add constraint ck_stock_movements_type check (
        movement_type in ('opening', 'adjustment', 'transfer', 'purchase', 'sale')
    ),
    add constraint ck_stock_movements_reason check (
        (movement_type in ('opening', 'adjustment', 'purchase', 'sale') and char_length(btrim(reason)) > 0)
        or movement_type = 'transfer'
    ),
    add constraint ck_stock_movements_purchase_direction check (
        movement_type <> 'purchase' or direction = 'in'
    ),
    add constraint ck_stock_movements_sale_direction check (
        movement_type <> 'sale' or direction = 'out'
    );

create table if not exists sale_receipt_counter (
    id integer not null,
    last_number integer not null,
    constraint pk_sale_receipt_counter primary key (id),
    constraint ck_sale_receipt_counter_singleton check (id = 1),
    constraint ck_sale_receipt_counter_last_number check (last_number >= 0)
);

insert into sale_receipt_counter (id, last_number)
select 1, 0
where not exists (select 1 from sale_receipt_counter where id = 1);

create table if not exists sales (
    id uuid not null,
    receipt_number integer not null,
    operation_id uuid not null,
    operation_fingerprint text not null,
    sold_at timestamp with time zone not null,
    payment_method character varying(16) not null,
    total numeric not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_sales primary key (id),
    constraint uq_sales_receipt_number unique (receipt_number),
    constraint uq_sales_operation_id unique (operation_id),
    constraint ck_sales_receipt_number check (receipt_number > 0),
    constraint ck_sales_fingerprint check (char_length(operation_fingerprint) > 0),
    constraint ck_sales_payment_method check (payment_method = 'cash'),
    constraint ck_sales_total_scale check (
        total = round(total, 2)
        and total >= 0
        and total <= 9999999999.99
    ),
    constraint ck_sales_not_deleted check (is_deleted = false)
);

create index if not exists ix_sales_sold_at
    on sales (sold_at desc, id);

create table if not exists sale_lines (
    id uuid not null,
    sale_id uuid not null,
    product_id uuid not null,
    shelf_id uuid not null,
    line_number integer not null,
    product_name character varying(200) not null,
    quantity numeric not null,
    unit_price numeric not null,
    line_total numeric not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_sale_lines primary key (id),
    constraint fk_sale_lines_sales_sale_id foreign key (sale_id) references sales (id),
    constraint fk_sale_lines_products_product_id foreign key (product_id) references products (id),
    constraint fk_sale_lines_shelves_shelf_id foreign key (shelf_id) references shelves (id),
    constraint ck_sale_lines_number check (line_number >= 1),
    constraint ck_sale_lines_product_name check (
        product_name = btrim(product_name) and char_length(product_name) between 1 and 200
    ),
    constraint ck_sale_lines_quantity_scale check (
        quantity = round(quantity, 3)
        and quantity > 0
        and quantity <= 99999999999.999
    ),
    constraint ck_sale_lines_unit_price_scale check (
        unit_price = round(unit_price, 2)
        and unit_price >= 0
        and unit_price <= 9999999999.99
    ),
    constraint ck_sale_lines_line_total check (
        line_total = round(quantity * unit_price, 2)
        and line_total >= 0
        and line_total <= 9999999999.99
    ),
    constraint ck_sale_lines_not_deleted check (is_deleted = false)
);

create unique index if not exists uq_sale_lines_number
    on sale_lines (sale_id, line_number)
    where is_deleted = false;

create index if not exists ix_sale_lines_sale_id
    on sale_lines (sale_id);

create index if not exists ix_sale_lines_product_id
    on sale_lines (product_id);
