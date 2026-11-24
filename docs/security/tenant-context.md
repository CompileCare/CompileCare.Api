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
