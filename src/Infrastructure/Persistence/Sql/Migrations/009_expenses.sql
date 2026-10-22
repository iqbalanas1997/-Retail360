-- Shop expenses. This script does not change stock, purchases, suppliers, or sales.
-- Do not edit this script after it has been applied. Add a new numbered script.

create table if not exists expenses (
    id uuid not null,
    operation_id uuid not null,
    operation_fingerprint text not null,
    category character varying(32) not null,
    amount numeric(12,2) not null,
    expense_date date not null,
    notes character varying(1000) null,
    payment_method character varying(16) not null,
    created_at timestamp with time zone not null,
    updated_at timestamp with time zone not null,
    is_deleted boolean not null,
    constraint pk_expenses primary key (id),
    constraint uq_expenses_operation_id unique (operation_id),
    constraint ck_expenses_fingerprint check (char_length(operation_fingerprint) > 0),
    constraint ck_expenses_category check (
        category in ('rent', 'utilities', 'wages', 'transport', 'supplies', 'repairs', 'other')),
    constraint ck_expenses_amount check (
        amount = round(amount, 2)
        and amount > 0
        and amount <= 9999999999.99),
    constraint ck_expenses_notes check (
        notes is null
        or (char_length(btrim(notes)) > 0 and char_length(notes) <= 1000)),
    constraint ck_expenses_payment_method check (payment_method = 'cash')
);

create index if not exists ix_expenses_date
    on expenses (expense_date desc, id desc)
    where is_deleted = false;
