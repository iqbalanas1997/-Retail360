-- Supplier master data. This script does not create purchases, payments, or balances.
-- Do not edit this script after it has been applied. Add a new numbered script.

create table if not exists suppliers (
    id uuid not null,
    name character varying(200) not null,
    contact_person character varying(200) null,
    phone character varying(40) null,
    email character varying(200) null,
    address character varying(400) null,
    notes character varying(1000) null,
    is_active boolean not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_suppliers primary key (id),
    constraint ck_suppliers_name check (char_length(btrim(name)) > 0 and char_length(name) <= 200),
    constraint ck_suppliers_contact_person check (
        contact_person is null
        or (char_length(btrim(contact_person)) > 0 and char_length(contact_person) <= 200)),
    constraint ck_suppliers_phone check (
        phone is null
        or (char_length(btrim(phone)) > 0 and char_length(phone) <= 40)),
    constraint ck_suppliers_email check (
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
    constraint ck_suppliers_address check (
        address is null
        or (char_length(btrim(address)) > 0 and char_length(address) <= 400)),
    constraint ck_suppliers_notes check (
        notes is null
        or (char_length(btrim(notes)) > 0 and char_length(notes) <= 1000))
);

create unique index if not exists uq_suppliers_name
    on suppliers (lower(name))
    where is_deleted = false;
