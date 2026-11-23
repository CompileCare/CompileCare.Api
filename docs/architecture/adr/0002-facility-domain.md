# ADR-0002: Facility domain as the first persisted business module

- Status: Accepted
- Date: 2026-11-23

## Decision
Start the first persisted business capability with a tenant-owned Facility (hospital/clinic) entity. Facility records have a tenant-scoped unique code, standard audit metadata inherited from TenantOwnedEntity, and soft-state via IsActive. The first API slice exposes list, get, create, update, and delete endpoints under /api/v1/facilities.

## Rationale
Facilities are the root of the healthcare physical/organizational hierarchy and provide the parent for subsequent Department, Ward, Room, and Bed entities. This keeps the initial model small while establishing persistence, validation, tenant scoping, and endpoint conventions.

## Security
Tenant query filters and persistence tenant validation remain defense-in-depth. The current tenant header is not authentication or authorization. Facility APIs must not be considered production-secure until tenant context is bound to an authenticated principal and membership is authorized.
