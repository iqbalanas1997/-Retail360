-- Give every account a security stamp so an access token can be checked against the account.
-- A password change replaces this value. Existing tokens that carry the old value are rejected.
-- This does not add a session table, and it does not edit 001_initial_identity.sql.

update asp_net_users
set security_stamp = gen_random_uuid()::text
where security_stamp is null
   or btrim(security_stamp) = '';
