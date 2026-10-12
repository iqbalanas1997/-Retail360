-- Inventory locations, shelf balances, and the append-only movement ledger.
-- Do not edit this script after it has been applied. Add a new numbered script.

create table if not exists areas (
    id uuid not null,
    name character varying(200) not null,
    is_active boolean not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_areas primary key (id),
    constraint ck_areas_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200)
);

create table if not exists shelves (
    id uuid not null,
    area_id uuid not null,
    name character varying(200) not null,
    is_active boolean not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_shelves primary key (id),
    constraint fk_shelves_areas_area_id foreign key (area_id) references areas (id),
    constraint ck_shelves_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200)
);

create table if not exists stock_balances (
    id uuid not null,
    product_id uuid not null,
    shelf_id uuid not null,
    quantity numeric(14,3) not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    constraint pk_stock_balances primary key (id),
    constraint fk_stock_balances_products_product_id foreign key (product_id) references products (id),
    constraint fk_stock_balances_shelves_shelf_id foreign key (shelf_id) references shelves (id),
    constraint uq_stock_balances_product_shelf unique (product_id, shelf_id),
    constraint ck_stock_balances_quantity check (quantity >= 0)
);

create table if not exists stock_movements (
    id uuid not null,
    operation_id uuid not null,
    product_id uuid not null,
    shelf_id uuid not null,
    quantity numeric(14,3) not null,
    direction character varying(3) not null,
    movement_type character varying(16) not null,
    reason character varying(200) not null,
    other_shelf_id uuid null,
    occurred_at timestamp with time zone not null,
    created_at timestamp with time zone not null,
    constraint pk_stock_movements primary key (id),
    constraint fk_stock_movements_products_product_id foreign key (product_id) references products (id),
    constraint fk_stock_movements_shelves_shelf_id foreign key (shelf_id) references shelves (id),
    constraint fk_stock_movements_shelves_other_shelf_id foreign key (other_shelf_id) references shelves (id),
    constraint ck_stock_movements_quantity check (quantity > 0),
    constraint ck_stock_movements_direction check (direction in ('in', 'out')),
    constraint ck_stock_movements_type check (movement_type in ('opening', 'adjustment', 'transfer')),
    constraint ck_stock_movements_reason check (
        (movement_type in ('opening', 'adjustment') and char_length(btrim(reason)) > 0)
        or movement_type = 'transfer'
    ),
    constraint ck_stock_movements_opening_direction check (
        movement_type <> 'opening' or direction = 'in'
    ),
    constraint ck_stock_movements_transfer_shelves check (
        (movement_type = 'transfer' and other_shelf_id is not null and other_shelf_id <> shelf_id)
        or (movement_type <> 'transfer' and other_shelf_id is null)
    )
);

create unique index if not exists uq_areas_name
    on areas (lower(name))
    where is_deleted = false;

create unique index if not exists uq_shelves_area_name
    on shelves (area_id, lower(name))
    where is_deleted = false;

create index if not exists ix_shelves_area_id on shelves (area_id);
create index if not exists ix_stock_balances_product_id on stock_balances (product_id);
create index if not exists ix_stock_balances_shelf_id on stock_balances (shelf_id);
create index if not exists ix_stock_movements_product_id on stock_movements (product_id);
create index if not exists ix_stock_movements_shelf_id on stock_movements (shelf_id);
create index if not exists ix_stock_movements_operation_id on stock_movements (operation_id);

create or replace function prevent_stock_movement_change()
returns trigger
language plpgsql
as $$
begin
    raise exception 'stock_movements is append-only'
        using errcode = '55000';
end;
$$;

drop trigger if exists stock_movements_append_only on stock_movements;

create trigger stock_movements_append_only
    before update or delete on stock_movements
    for each row
    execute function prevent_stock_movement_change();
