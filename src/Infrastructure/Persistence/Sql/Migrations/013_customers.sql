-- Customer master data and an optional customer on a cash sale.
-- This script does not create credit, receivables, payments, or balances.
-- Do not edit this script after it has been applied. Add a new numbered script.

create table if not exists customers (
    id uuid not null,
    name character varying(200) not null,
    phone character varying(40) null,
    email character varying(200) null,
    address character varying(400) null,
    notes character varying(1000) null,
    is_active boolean not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_customers primary key (id),
    constraint ck_customers_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200),
    constraint ck_customers_phone check (
        phone is null
        or (char_length(btrim(phone)) > 0 and char_length(phone) <= 40)),
    constraint ck_customers_email check (
        email is null
        or (
            char_length(email) between 1 and 200
            and email = btrim(email)
            and email !~ '[[:space:]]'
            and char_length(email) - char_length(replace(email, '@', '')) = 1
            and position('@' in email) > 1
            and char_length(split_part(email, '@', 2)) >= 3
            and position('.' in split_part(email, '@', 2)) > 1
            and right(split_part(email, '@', 2), 1) <> '.')),
    constraint ck_customers_address check (
        address is null
        or (char_length(btrim(address)) > 0 and char_length(address) <= 400)),
    constraint ck_customers_notes check (
        notes is null
        or (char_length(btrim(notes)) > 0 and char_length(notes) <= 1000))
);

create index if not exists ix_customers_name
    on customers (lower(name), id)
    where is_deleted = false;

create index if not exists ix_customers_phone
    on customers (phone)
    where is_deleted = false and phone is not null;

create index if not exists ix_customers_email
    on customers (lower(email))
    where is_deleted = false and email is not null;

alter table sales
    add column if not exists customer_id uuid null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'fk_sales_customers_customer_id'
    ) then
        alter table sales
            add constraint fk_sales_customers_customer_id
            foreign key (customer_id) references customers (id);
    end if;
end $$;

create index if not exists ix_sales_customer_id
    on sales (customer_id, sold_at desc, id desc)
    where customer_id is not null;
