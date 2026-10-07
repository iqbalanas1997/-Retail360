---
name: database-change
description: Safely design and implement PostgreSQL schema changes with SQL scripts. Dapper and Npgsql are the persistence stack. Do not add EF Core.
paths: "**/*Migration*.cs,**/*.cs,**/*.sql"
---
# Database Change Skill

1. Read `docs/database/DATABASE.md` and ADR-009.
2. Read `docs/domain/DOMAIN_MODEL.md`.
3. Search existing SQL scripts and persistence code.
4. Confirm whether the change is new, additive, corrective or destructive.
5. Never edit a SQL script that may already have been applied. Add the next numbered script under `src/Infrastructure/Persistence/Sql/Migrations`.
6. Review the script for data-loss or destructive operations.
7. For audited tables, set `created_at` and `updated_at` in inserts, set `updated_at` in updates, and filter active reads with `is_deleted = false`. Soft delete is an update, not a SQL `DELETE`. There is no global query filter.
8. Update domain/database documentation when the schema meaning changes.
