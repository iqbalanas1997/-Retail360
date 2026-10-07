# API Contract Rules

This document starts as a contract policy and becomes a concrete endpoint catalog as APIs are implemented.

## Rules
- REST/JSON.
- Consistent HTTP status codes.
- DTOs are the public contract.
- Pagination/filtering conventions should be standardized before many list endpoints are created.
- Error responses must be consistent.
- Do not expose database entities directly.
- Avoid duplicate endpoints that perform the same use case.

## Endpoint catalog
No business endpoints are implemented.

The Web API template enables OpenAPI in Development at `/swagger`. That UI is host tooling, not a business contract.
