# AI Context Protocol

This file is the compact "map" for Cursor when a task is large.

## Product
Grocery Shop Management System.

## Stack
.NET 8 + modern Angular + PostgreSQL + Dapper + Npgsql.

## Architecture
Modular monolith with Domain/Application/Infrastructure/API boundaries.

## Primary UX
Simple single-screen POS/counter workflow.

## Persistent knowledge
- Requirements: `docs/requirements/REQUIREMENTS.md`
- Architecture: `docs/architecture/ARCHITECTURE.md`
- Decisions: `docs/architecture/DECISIONS.md`
- Domain: `docs/domain/DOMAIN_MODEL.md`
- Modules: `docs/project/MODULE_CATALOG.md`
- Tasks: `docs/project/TASKS.md`
- POS UX: `docs/ui/POS_UX.md`
- Database: `docs/database/DATABASE.md`
- API: `docs/api/API_CONTRACTS.md`

## Context protocol
Never assume a previous chat is the source of truth. The repository is the source of truth.

Before substantial work:
- read the relevant docs;
- search the codebase;
- identify existing reusable code;
- identify affected modules;
- check current task status.

After substantial work:
- verify;
- update affected documentation;
- update task status;
- record important decisions.
