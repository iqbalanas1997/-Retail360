# Grocery Shop Management System — Agent Instructions

## Mission
Build a simple, reliable Grocery Shop Management System for a small retail shop.

## Technology baseline
- Backend: .NET 8 Web API (LTS baseline)
- Frontend: modern Angular (NOT legacy AngularJS 1.x)
- Database: PostgreSQL
- Persistence: Dapper + Npgsql. Do not use EF Core.
- API style: REST/JSON
- Architecture: modular monolith with clear Domain/Application/Infrastructure/API boundaries
- UI priority: extremely simple counter/POS workflow, minimal screens and controls.

## Source-of-truth order
1. `docs/requirements/REQUIREMENTS.md`
2. `docs/architecture/ARCHITECTURE.md`
3. `docs/domain/DOMAIN_MODEL.md`
4. `docs/architecture/DECISIONS.md`
5. `docs/project/MODULE_CATALOG.md`
6. `docs/project/TASKS.md`
7. Existing source code and database migrations
8. Chat instructions for the current task

If documentation conflicts with existing code, do NOT silently redesign the system. Explain the conflict and propose the smallest safe change.

## Non-negotiable agent behavior
- Search the existing codebase before creating a new class, component, service, endpoint, DTO, repository, helper, table, or migration.
- Reuse existing abstractions and patterns whenever they fit.
- Never create duplicate functionality under a different name.
- Never invent requirements. Mark assumptions explicitly.
- Before a multi-file change, make a short implementation plan.
- Keep changes focused on the requested task; do not perform unrelated refactoring.
- Do not change architecture or technology without explicit approval.
- After changes, run the relevant build/tests and report what was verified.
- Update project documentation when an architectural decision, module, API, database structure, or workflow changes.
- Preserve existing behavior unless the task explicitly changes it.
- Prefer the simplest implementation that satisfies the requirement.

## Context continuity
At the start of a substantial task:
1. Read this file.
2. Read the relevant requirement/architecture/domain docs.
3. Inspect existing code before proposing new code.
4. Check `docs/project/TASKS.md` and `docs/project/MODULE_CATALOG.md`.
5. State which existing modules/patterns will be reused.
6. At the end, update task status and relevant documentation.

## Stop conditions
Stop and ask for clarification when:
- a requirement is ambiguous and the choice changes business behavior;
- two existing patterns conflict;
- a destructive database change is required;
- a new dependency is needed but not already approved;
- a request would violate the architecture or create duplicate concepts.

## UI principle
The POS/counter workflow must remain fast and simple:
- scan/search product
- enter quantity
- review cart
- take payment
- print/show receipt

Avoid unnecessary popups, fields, navigation and visual noise.
