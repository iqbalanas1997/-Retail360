-- A staff account can be deactivated without being soft-deleted.
-- Do not edit this script after it has been applied.

alter table asp_net_users
    add column if not exists is_active boolean;

update asp_net_users
set is_active = true
where is_active is null;

alter table asp_net_users
    alter column is_active set default true;

alter table asp_net_users
    alter column is_active set not null;
