---
name: feature-development
description: End-to-end workflow for implementing a grocery system feature without losing context or creating duplicate code.
---
# Feature Development Skill

## Before coding
- Read `AGENTS.md`.
- Read the relevant requirement, architecture, domain and module docs.
- Search for existing equivalent functionality.
- Identify backend, frontend, database and test impact.
- Write a short implementation plan.

## During coding
- Reuse existing patterns.
- Keep the change focused.
- Avoid unrelated refactoring.
- Keep API, database and UI contracts consistent.

## After coding
- Build affected projects.
- Run relevant tests.
- Inspect the diff.
- Update `TASKS.md`, `MODULE_CATALOG.md`, API/database docs and ADRs if needed.
- Report what was verified and any remaining uncertainty.
