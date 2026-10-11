-- Catalog master data.
-- Do not edit this script after it has been applied. Add a new numbered script.

create table if not exists categories (
    id uuid not null,
    name character varying(200) not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_categories primary key (id),
    constraint ck_categories_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200)
);

create table if not exists brands (
    id uuid not null,
    name character varying(200) not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_brands primary key (id),
    constraint ck_brands_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200)
);

create table if not exists units (
    id uuid not null,
    name character varying(200) not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_units primary key (id),
    constraint ck_units_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200)
);

create table if not exists products (
    id uuid not null,
    name character varying(200) not null,
    category_id uuid null,
    brand_id uuid null,
    unit_id uuid not null,
    purchase_price numeric(12,2) not null,
    selling_price numeric(12,2) not null,
    minimum_stock numeric(14,3) not null,
    is_active boolean not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_products primary key (id),
    constraint fk_products_categories_category_id
        foreign key (category_id) references categories (id),
    constraint fk_products_brands_brand_id
        foreign key (brand_id) references brands (id),
    constraint fk_products_units_unit_id
        foreign key (unit_id) references units (id),
    constraint ck_products_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200),
    constraint ck_products_purchase_price check (purchase_price >= 0),
    constraint ck_products_selling_price check (selling_price >= 0),
    constraint ck_products_minimum_stock check (minimum_stock >= 0)
);

create table if not exists product_barcodes (
    id uuid not null,
    product_id uuid not null,
    barcode character varying(128) not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_product_barcodes primary key (id),
    constraint fk_product_barcodes_products_product_id
        foreign key (product_id) references products (id),
    constraint ck_product_barcodes_barcode check (char_length(btrim(barcode)) > 0 and char_length(barcode) <= 128)
);

create unique index if not exists uq_categories_name
    on categories (lower(name))
    where is_deleted = false;

create unique index if not exists uq_brands_name
    on brands (lower(name))
    where is_deleted = false;

create unique index if not exists uq_units_name
    on units (lower(name))
    where is_deleted = false;

create index if not exists ix_products_category_id on products (category_id);
create index if not exists ix_products_brand_id on products (brand_id);
create index if not exists ix_products_unit_id on products (unit_id);

create unique index if not exists uq_product_barcodes_barcode
    on product_barcodes (barcode)
    where is_deleted = false;

create index if not exists ix_product_barcodes_product_id on product_barcodes (product_id);
