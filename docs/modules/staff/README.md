# Staff management API

The staff module maintains tenant-owned workforce profiles and effective-dated facility assignments. It deliberately does not create login accounts, grant roles, verify professional credentials, or store payroll/government identifiers.

All routes require a validated OIDC identity, an active tenant membership and the X-Tenant-ID request context. Staff permissions are granted to the HospitalAdmin role in this first increment; other roles do not inherit them implicitly.

## Routes

- POST /api/v1/staff — create a profile with its first hospital/department assignment.
- GET /api/v1/staff?page=1&pageSize=50&query=... — list/search within the caller's authorized organization scope; page size is capped at 100.
- GET /api/v1/staff/{staffId} — retrieve a profile and visible assignments.
- PUT /api/v1/staff/{staffId} — update permitted profile fields.
- POST /api/v1/staff/{staffId}/assignments — add an assignment.
- POST /api/v1/staff/{staffId}/assignments/{assignmentId}/end — end-date an assignment.
- POST /api/v1/staff/{staffId}/deactivate and /reactivate — change profile lifecycle.

The create request contains staffCode, givenName, optional middleName, familyName, optional providerDesignation, hospitalId, optional departmentId and optional effectiveFrom. The API derives tenant ownership from the authenticated request and validates all facility references against the tenant.

Provider designation is operational metadata only. It does not indicate credential, licence, qualification or clinical privilege verification. Profile-to-membership linking, credentialing, payroll, attendance, rostering, sensitive identifiers and contact data are deferred.

Every profile and assignment is tenant-owned. Composite tenant-aware foreign keys, tenant query filters, organization-scope checks, bounded pagination and the request audit pipeline apply. Profiles are deactivated rather than physically deleted; assignments retain their effective-date history.
