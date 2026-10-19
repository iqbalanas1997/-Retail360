-- Purchase documents and the purchase stock-movement type.
-- Do not edit earlier scripts. Opening, adjustment, and transfer checks stay in force.
-- Rollback: drop purchase_lines and purchases, then restore the three stock_movements
-- checks named below to the 003_inventory.sql definitions and drop
-- ck_stock_movements_purchase_direction.

alter table stock_movements drop constraint ck_stock_movements_type;
alter table stock_movements drop constraint ck_stock_movements_reason;

alter table stock_movements
    add constraint ck_stock_movements_type check (
        movement_type in ('opening', 'adjustment', 'transfer', 'purchase')
    ),
    add constraint ck_stock_movements_reason check (
        (movement_type in ('opening', 'adjustment', 'purchase') and char_length(btrim(reason)) > 0)
        or movement_type = 'transfer'
    ),
    add constraint ck_stock_movements_purchase_direction check (
        movement_type <> 'purchase' or direction = 'in'
    );

create table if not exists purchases (
    id uuid not null,
    supplier_id uuid not null,
    purchase_date date not null,
    reference character varying(40) null,
    notes character varying(1000) null,
    status character varying(16) not null,
    total numeric not null,
    receive_operation_id uuid null,
    receive_fingerprint text null,
    received_at timestamp with time zone null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_purchases primary key (id),
    constraint fk_purchases_suppliers_supplier_id foreign key (supplier_id) references suppliers (id),
    constraint ck_purchases_status check (status in ('recorded', 'received')),
    constraint ck_purchases_reference check (
        reference is null
        or (reference = btrim(reference) and char_length(reference) between 1 and 40)
    ),
    constraint ck_purchases_notes check (
        notes is null
        or (notes = btrim(notes) and char_length(notes) between 1 and 1000)
    ),
    constraint ck_purchases_total_scale check (
        total = round(total, 2)
        and total >= 0
        and total <= 9999999999.99
    ),
    constraint ck_purchases_receive check (
        (
            status = 'recorded'
            and receive_operation_id is null
            and receive_fingerprint is null
            and received_at is null
        )
        or (
            status = 'received'
            and receive_operation_id is not null
            and receive_fingerprint is not null
            and char_length(receive_fingerprint) > 0
            and received_at is not null
        )
    ),
    constraint ck_purchases_received_not_deleted check (not is_deleted or status = 'recorded')
);

create unique index if not exists uq_purchases_receive_operation
    on purchases (receive_operation_id)
    where receive_operation_id is not null;

create index if not exists ix_purchases_supplier_id
    on purchases (supplier_id)
    where is_deleted = false;

create table if not exists purchase_lines (
    id uuid not null,
    purchase_id uuid not null,
    product_id uuid not null,
    line_number integer not null,
    quantity numeric not null,
    unit_cost numeric not null,
    line_total numeric not null,
    shelf_id uuid null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_purchase_lines primary key (id),
    constraint fk_purchase_lines_purchases_purchase_id foreign key (purchase_id) references purchases (id),
    constraint fk_purchase_lines_products_product_id foreign key (product_id) references products (id),
    constraint fk_purchase_lines_shelves_shelf_id foreign key (shelf_id) references shelves (id),
    constraint ck_purchase_lines_number check (line_number >= 1),
    constraint ck_purchase_lines_quantity_scale check (
        quantity = round(quantity, 3)
        and quantity > 0
        and quantity <= 99999999999.999
    ),
    constraint ck_purchase_lines_unit_cost_scale check (
        unit_cost = round(unit_cost, 2)
        and unit_cost >= 0
        and unit_cost <= 9999999999.99
    ),
    constraint ck_purchase_lines_total_scale check (
        line_total = round(line_total, 2)
        and line_total = round(quantity * unit_cost, 2)
        and line_total >= 0
        and line_total <= 9999999999.99
    )
);

create unique index if not exists uq_purchase_lines_number
    on purchase_lines (purchase_id, line_number)
    where is_deleted = false;

create index if not exists ix_purchase_lines_purchase_id on purchase_lines (purchase_id);
create index if not exists ix_purchase_lines_product_id on purchase_lines (product_id);
create index if not exists ix_purchase_lines_shelf_id on purchase_lines (shelf_id);
