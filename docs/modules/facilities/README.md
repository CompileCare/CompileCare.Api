# Facility hierarchy module

The healthcare organization and physical-location hierarchy is:

**Hospital → Department → Ward → Room → Bed**

All entities inherit tenant ownership, audit timestamps, and optimistic-concurrency versioning. Composite tenant-scoped foreign keys prevent child records from referencing a parent belonging to another tenant. Restrictive delete relationships protect hierarchy integrity.

## API

All routes are versioned under `/api/v1`.

| Resource | Collection route |
|---|---|
| Hospitals | `/hospitals` |
| Departments | `/hospitals/{hospitalId}/departments` |
| Wards | `/departments/{departmentId}/wards` |
| Rooms | `/wards/{wardId}/rooms` |
| Beds | `/rooms/{roomId}/beds` |

Each resource collection supports:

- `GET` to list a page
- `POST` to create a resource
- `GET /{id}` to retrieve a resource
- `PUT /{id}` to update a resource
- `DELETE /{id}` to delete a resource

Collection endpoints accept `page` and `pageSize`. The default page size is 50 and the maximum is 100. Results use deterministic ordering with the resource identifier as a tie-breaker and include pagination metadata (`page`, `pageSize`, `totalCount`, and `totalPages`).

Successful create operations return `201 Created` with a `Location` header pointing to the created resource. Duplicate natural keys return `409 Conflict`; a missing or out-of-scope parent returns `404 Not Found`. A parent cannot be deleted while it contains child resources.

Hospital names and optional codes are unique within a tenant. Department names and optional codes are unique within their hospital. Ward names, room names, and bed numbers are unique within their immediate parent.

## Architecture

The API layer defines route groups and HTTP request/response mapping. Application contracts expose resource-specific service interfaces, DTOs, inputs, result types, and bounded pagination. Infrastructure implements those contracts using EF Core and PostgreSQL. The shared `FacilityService` is registered once per request scope and exposed through the resource-specific interfaces.

## Security

Tenant filters, persistence checks, tenant-scoped unique indexes, and composite foreign keys are defense-in-depth. The `X-Tenant-ID` header is context only; it is not authentication or authorization. Before production use, tenant identity must be derived from an authenticated principal and authorized against that principal's memberships. Never treat a client-supplied tenant header as proof of access.
