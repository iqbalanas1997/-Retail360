---
name: database-change
description: Safely design and implement PostgreSQL/EF Core schema changes and migrations.
paths: "**/*Migration*.cs,**/*.cs,**/*.sql"
---
# Database Change Skill

1. Read `docs/database/DATABASE.md`.
2. Read `docs/domain/DOMAIN_MODEL.md`.
3. Search existing entities, configurations and migrations.
4. Confirm whether the change is new, additive, corrective or destructive.
5. Never edit an already-applied migration to represent a new requirement.
6. Generate a new EF Core migration.
7. Review the migration for data-loss or destructive operations.
8. Update domain/database documentation when the schema meaning changes.
