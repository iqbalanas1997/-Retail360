-- Stock operation keys and the transfer-history lookup index.
-- Do not edit this script after it has been applied. Add a new numbered script.

create table if not exists stock_operations (
    id uuid not null,
    product_id uuid not null,
    fingerprint text not null,
    created_at timestamp with time zone not null,
    constraint pk_stock_operations primary key (id),
    constraint fk_stock_operations_products_product_id foreign key (product_id) references products (id),
    constraint ck_stock_operations_fingerprint check (char_length(fingerprint) > 0)
);

create index if not exists ix_stock_operations_product_id on stock_operations (product_id);

create index if not exists ix_stock_movements_other_shelf_id on stock_movements (other_shelf_id);
