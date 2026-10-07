# Cursor AI Context Pack

Copy this pack into the root of the Grocery Shop Management System repository.

It provides:
- `AGENTS.md` — project-wide instructions.
- `.cursor/rules/*.mdc` — persistent Cursor rules.
- `.cursor/skills/*/SKILL.md` — specialized workflows.
- `docs/*` — persistent project knowledge.

## Recommended first Cursor prompt

Paste this into a new Cursor Agent chat:

> Onboard yourself to this repository. Do not write application code yet.
> Read `AGENTS.md`, `docs/AI_CONTEXT.md`, `docs/requirements/REQUIREMENTS.md`,
> `docs/architecture/ARCHITECTURE.md`, `docs/architecture/DECISIONS.md`,
> `docs/domain/DOMAIN_MODEL.md`, `docs/project/MODULE_CATALOG.md`,
> `docs/project/TASKS.md`, `docs/ui/POS_UX.md`, `docs/database/DATABASE.md`,
> and `docs/api/API_CONTRACTS.md`.
>
> Then inspect the repository structure and summarize:
> 1. current architecture,
> 2. existing reusable modules,
> 3. current implementation status,
> 4. contradictions or missing decisions,
> 5. exact next recommended task.
>
> Do not create or modify source code. Only update documentation if you find a factual mismatch that can be confirmed from the repository.

## Important
Do not treat chat history as permanent project memory. Keep important decisions and current status in repository files.

Cursor's current documentation supports project rules in `.cursor/rules/*.mdc`, `AGENTS.md`, and project skills under `.cursor/skills/`. Rules are persistent instructions; skills are specialized workflows loaded when relevant.
