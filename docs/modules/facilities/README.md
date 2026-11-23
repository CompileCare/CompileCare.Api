# Facility hierarchy module

The first healthcare organization/location model is:

**Hospital → Department → Ward → Room → Bed**

All entities inherit tenant ownership, audit timestamps, and optimistic-concurrency versioning. Composite tenant-scoped foreign keys prevent child records from referencing a parent belonging to another tenant. Delete relationships are restrictive to protect hierarchy integrity.

## Initial API
Hospital endpoints:
- GET /api/v1/hospitals
- GET /api/v1/hospitals/{id}
- POST /api/v1/hospitals
- PUT /api/v1/hospitals/{id}
- DELETE /api/v1/hospitals/{id}

Hospital name and optional code are unique per tenant.

Tenant filters and persistence checks are defense-in-depth. The tenant header is context only, not authentication or authorization. APIs are not production-secure until tenant membership is bound to an authenticated principal and authorized.
