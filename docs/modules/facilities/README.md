# Facilities module

Facility represents a hospital, clinic, or healthcare site belonging to a tenant.

## Initial API
- GET /api/v1/facilities
- GET /api/v1/facilities/{id}
- POST /api/v1/facilities
- PUT /api/v1/facilities/{id}
- DELETE /api/v1/facilities/{id}

Code is normalized to uppercase and unique within the tenant. Tenant isolation is provided by the existing EF Core query filter and persistence checks. The tenant header is context only and is not authorization.

## Next hierarchy
Facility -> Department -> Ward -> Room -> Bed. The next slice should add Department with a required Facility relationship and tenant-safe parent validation.
