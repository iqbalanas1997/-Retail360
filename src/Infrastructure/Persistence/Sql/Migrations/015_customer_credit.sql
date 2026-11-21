-- Customer credit charges, payments, and allocations.
-- Do not edit earlier scripts. Existing sale and customer rows are not updated.
-- A later correction is a new compensating ledger entry. These tables reject UPDATE and DELETE.

alter table sales drop constraint ck_sales_payment_method;

alter table sales
    add constraint ck_sales_payment_method check (payment_method in ('cash', 'credit')),
    add constraint ck_sales_credit_customer check (
        payment_method <> 'credit' or customer_id is not null
    ),
    add constraint ck_sales_credit_total check (
        payment_method <> 'credit' or total > 0
    );

create table if not exists customer_ledger_entries (
    id uuid not null,
    operation_id uuid not null,
    operation_fingerprint text not null,
    customer_id uuid not null,
    sale_id uuid null,
    entry_type character varying(16) not null,
    amount numeric not null,
    occurred_at timestamp with time zone not null,
    created_at timestamp with time zone not null,
    constraint pk_customer_ledger_entries primary key (id),
    constraint uq_customer_ledger_entries_operation_id unique (operation_id),
    constraint fk_customer_ledger_entries_customers_customer_id
        foreign key (customer_id) references customers (id),
    constraint fk_customer_ledger_entries_sales_sale_id
        foreign key (sale_id) references sales (id),
    constraint ck_customer_ledger_entries_fingerprint check (char_length(operation_fingerprint) > 0),
    constraint ck_customer_ledger_entries_type check (entry_type in ('charge', 'payment')),
    constraint ck_customer_ledger_entries_amount check (
        amount = round(amount, 2)
        and amount > 0
        and amount <= 9999999999.99
    ),
    constraint ck_customer_ledger_entries_shape check (
        (entry_type = 'charge' and sale_id is not null)
        or (entry_type = 'payment' and sale_id is null)
    )
);

create unique index if not exists uq_customer_ledger_entries_charge_sale
    on customer_ledger_entries (sale_id)
    where entry_type = 'charge';

create index if not exists ix_customer_ledger_entries_customer
    on customer_ledger_entries (customer_id, occurred_at, id);

create table if not exists customer_payment_allocations (
    id uuid not null,
    payment_entry_id uuid not null,
    charge_entry_id uuid not null,
    amount numeric not null,
    created_at timestamp with time zone not null,
    constraint pk_customer_payment_allocations primary key (id),
    constraint uq_customer_payment_allocations_pair unique (payment_entry_id, charge_entry_id),
    constraint fk_customer_payment_allocations_payment
        foreign key (payment_entry_id) references customer_ledger_entries (id),
    constraint fk_customer_payment_allocations_charge
        foreign key (charge_entry_id) references customer_ledger_entries (id),
    constraint ck_customer_payment_allocations_distinct check (payment_entry_id <> charge_entry_id),
    constraint ck_customer_payment_allocations_amount check (
        amount = round(amount, 2)
        and amount > 0
        and amount <= 9999999999.99
    )
);

create index if not exists ix_customer_payment_allocations_charge
    on customer_payment_allocations (charge_entry_id);

create or replace function customer_credit_reject_change()
returns trigger
language plpgsql
as $$
begin
    raise exception 'customer credit ledger is append-only'
        using errcode = '55000';
end;
$$;

drop trigger if exists customer_ledger_entries_append_only on customer_ledger_entries;

create trigger customer_ledger_entries_append_only
    before update or delete on customer_ledger_entries
    for each row
    execute function customer_credit_reject_change();

drop trigger if exists customer_payment_allocations_append_only on customer_payment_allocations;

create trigger customer_payment_allocations_append_only
    before update or delete on customer_payment_allocations
    for each row
    execute function customer_credit_reject_change();

create or replace function customer_ledger_entries_guard_customer()
returns trigger
language plpgsql
as $$
declare
    active boolean;
    removed boolean;
begin
    select is_active, is_deleted
      into active, removed
      from customers
     where id = new.customer_id
     for update;

    if removed is null or removed then
        raise exception 'customer was not found'
            using errcode = '23503';
    end if;

    if new.entry_type = 'charge' and not active then
        raise exception 'customer is inactive'
            using errcode = '23514';
    end if;

    return new;
end;
$$;

drop trigger if exists customer_ledger_entries_customer on customer_ledger_entries;

create trigger customer_ledger_entries_customer
    before insert on customer_ledger_entries
    for each row
    execute function customer_ledger_entries_guard_customer();

create or replace function customer_credit_guard_payment()
returns trigger
language plpgsql
as $$
declare
    allocated numeric;
begin
    if new.entry_type <> 'payment' then
        return null;
    end if;

    perform 1
      from customers
     where id = new.customer_id
     for update;

    select coalesce(sum(amount), 0)
      into allocated
      from customer_payment_allocations
     where payment_entry_id = new.id;

    if allocated <> new.amount then
        raise exception 'payment allocations must equal the payment amount'
            using errcode = '23514';
    end if;

    return null;
end;
$$;

drop trigger if exists customer_ledger_entries_payment_balanced on customer_ledger_entries;

create constraint trigger customer_ledger_entries_payment_balanced
    after insert on customer_ledger_entries
    deferrable initially deferred
    for each row
    execute function customer_credit_guard_payment();

create or replace function customer_credit_guard_allocation()
returns trigger
language plpgsql
as $$
declare
    payment_amount numeric;
    payment_customer uuid;
    payment_type text;
    charge_amount numeric;
    charge_customer uuid;
    charge_type text;
    allocated_payment numeric;
    allocated_charge numeric;
begin
    select amount, customer_id, entry_type
      into payment_amount, payment_customer, payment_type
      from customer_ledger_entries
     where id = new.payment_entry_id
     for update;

    select amount, customer_id, entry_type
      into charge_amount, charge_customer, charge_type
      from customer_ledger_entries
     where id = new.charge_entry_id
     for update;

    if payment_type <> 'payment' or charge_type <> 'charge' or payment_customer <> charge_customer then
        raise exception 'a payment can settle only that customer''s charges'
            using errcode = '23514';
    end if;

    perform 1
      from customers
     where id = payment_customer
     for update;

    select coalesce(sum(amount), 0)
      into allocated_payment
      from customer_payment_allocations
     where payment_entry_id = new.payment_entry_id;

    if allocated_payment <> payment_amount then
        raise exception 'payment allocations must equal the payment amount'
            using errcode = '23514';
    end if;

    select coalesce(sum(amount), 0)
      into allocated_charge
      from customer_payment_allocations
     where charge_entry_id = new.charge_entry_id;

    if allocated_charge > charge_amount then
        raise exception 'allocations cannot exceed the charge'
            using errcode = '23514';
    end if;

    return null;
end;
$$;

drop trigger if exists customer_payment_allocations_balanced on customer_payment_allocations;

create constraint trigger customer_payment_allocations_balanced
    after insert on customer_payment_allocations
    deferrable initially deferred
    for each row
    execute function customer_credit_guard_allocation();

create or replace function customers_block_delete_with_balance()
returns trigger
language plpgsql
as $$
declare
    outstanding numeric;
begin
    if new.is_deleted and not old.is_deleted then
        select coalesce(sum(case when entry_type = 'charge' then amount else -amount end), 0)
          into outstanding
          from customer_ledger_entries
         where customer_id = new.id;

        if outstanding > 0 then
            raise exception 'customer still has an outstanding balance'
                using errcode = '23514';
        end if;
    end if;

    return new;
end;
$$;

drop trigger if exists customers_outstanding_blocks_delete on customers;

create trigger customers_outstanding_blocks_delete
    before update of is_deleted on customers
    for each row
    execute function customers_block_delete_with_balance();
