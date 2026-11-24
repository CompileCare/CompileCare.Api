# Tenant context and isolation

Tenant isolation is a mandatory platform invariant.

The API reads exactly one `X-Tenant-ID` GUID on tenant-scoped routes into a request-scoped `ITenantContext`. Invalid or missing values are rejected. The header only selects a tenant; it never grants access.

The platform tenant registry is global (not tenant-filtered). A tenant has an explicit lifecycle: `Provisioning`, `Active`, `Suspended`, or `Closed`. Only an `Active` tenant with an active issuer/subject membership can enter tenant-scoped routes. Suspended and closed tenants are denied even when a membership row remains active.

Tenant creation and lifecycle operations are platform-operator-only. Platform operator access is resolved from a persisted `platform_operators` record using the validated OIDC issuer and `sub`; token role/permission claims and client headers do not grant platform privileges. The first platform operator must be bootstrapped out-of-band by an authorised database administrator. No public platform-operator self-enrolment route exists.

After an active tenant membership is resolved, the API also loads its persisted organization scopes. Supported scope types are tenant-wide, one hospital, or one department (including the owning hospital). The department-to-hospital relationship is validated by composite foreign keys.

A membership with no scope is denied. Existing memberships are migrated to a tenant-wide scope to preserve their previously granted access; newly provisioned memberships must specify explicit scope. Tenant HospitalAdmins can administer memberships only inside their assigned scope. They cannot modify or deactivate themselves, and the last active tenant-wide HospitalAdmin cannot be removed. Tenant roles never grant platform-operator privileges.

Facility queries apply the organization scope inside the application service, not just in the UI or route authorization. Department-scoped users can read the parent hospital context but cannot mutate that hospital or other departments. Hospital- and tenant-scoped users can access their corresponding child facility hierarchy.

Before tenant-owned data access:
- Authenticate the caller.
- Resolve allowed tenant memberships from a trusted identity source.
- Verify the selected tenant is permitted for the caller and operation.
- Enforce tenant scope in every persistence path.
- Include tenant context in audit records.
- Propagate trusted tenant context to messages and background jobs.
- Test cross-tenant reads and writes explicitly.

Never derive tenant authorization solely from a client-supplied header, query string or route value.


## Current-user tenant context discovery

Authenticated staff clients can discover their own active tenant memberships before selecting a tenant:

- `GET /api/v1/me/tenant-contexts?page=1&pageSize=50`
- Requires a validated OIDC bearer token; no `X-Tenant-ID` header is accepted or needed.
- Resolves memberships only by the validated issuer and `sub` claims. External role/permission claims are ignored.
- Returns active memberships for active tenants, with the persisted CompileCare role and organization scopes. Results are bounded and paginated.
- Does not create or select a tenant context for the request. The client must choose one of the returned tenant IDs and then send that ID as `X-Tenant-ID` on subsequent tenant-scoped API requests.
- The endpoint is user-scoped and its reads, denials and failures are written to the metadata-only audit log. No membership credentials, request bodies or patient data are included.

The UI may use the returned scope data to present a tenant/facility selection experience, but it must not infer access from the selection itself. The API re-resolves the membership and enforces its active tenant lifecycle, role and hospital/department scope on every tenant-scoped request.
