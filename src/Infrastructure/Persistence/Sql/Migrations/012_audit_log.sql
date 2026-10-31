-- Append-only record of successful shop actions.
-- Do not edit this script after it has been applied.

create table if not exists audit_entries (
    id uuid primary key,
    occurred_at timestamptz not null,
    actor_user_id text null,
    action text not null,
    entity_type text not null,
    entity_id text not null,
    details jsonb not null
);

create index if not exists ix_audit_entries_occurred_at
    on audit_entries (occurred_at desc, id desc);

create index if not exists ix_audit_entries_actor_user_id
    on audit_entries (actor_user_id);

create index if not exists ix_audit_entries_action
    on audit_entries (action);

create index if not exists ix_audit_entries_entity
    on audit_entries (entity_type, entity_id);
