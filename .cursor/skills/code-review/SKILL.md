---
name: code-review
description: Review grocery system changes for duplication, architecture violations, regressions, security problems and unnecessary complexity.
---
# Code Review Skill

Check:
- duplicated classes/components/services/endpoints;
- wrong dependency direction;
- business logic in controllers/components;
- direct entity exposure from APIs;
- unsafe SQL or secrets;
- missing validation;
- inventory integrity;
- monetary precision;
- missing regression tests;
- unrelated refactoring;
- requirements drift;
- documentation drift.

Return findings grouped by severity and include file references.
