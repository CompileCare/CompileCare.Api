# ADR-0002: Hospital hierarchy as the first persisted business module

- Status: Accepted
- Date: 2026-11-23

## Decision
Establish the tenant-owned hierarchy Hospital → Department → Ward → Room → Bed as the first persisted business domain. Each entity includes audit/concurrency fields inherited from TenantOwnedEntity. Composite tenant-scoped foreign keys enforce same-tenant parent-child relationships. The first API slice provides CRUD for Hospital.

## Rationale
The hierarchy forms the foundation for healthcare facility operations and gives later domains (patients, admissions, appointments, staff, and billing) a stable physical/organizational context.

## Security
Tenant query filters and persistence tenant checks are defense-in-depth. The tenant header is not authentication or authorization; tenant membership must be validated against an authenticated principal before production access.
