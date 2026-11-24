# Tenant context and isolation

Tenant isolation is a mandatory platform invariant.

The API reads exactly one `X-Tenant-ID` GUID on tenant-scoped routes into a request-scoped `ITenantContext`. Invalid or missing values are rejected. The header only selects a tenant; it never grants access.

The platform tenant registry is global (not tenant-filtered). A tenant has an explicit lifecycle: `Provisioning`, `Active`, `Suspended`, or `Closed`. Only an `Active` tenant with an active issuer/subject membership can enter tenant-scoped routes. Suspended and closed tenants are denied even when a membership row remains active.

Tenant creation and lifecycle operations are platform-operator-only. Platform operator access is resolved from a persisted `platform_operators` record using the validated OIDC issuer and `sub`; token role/permission claims and client headers do not grant platform privileges. The first platform operator must be bootstrapped out-of-band by an authorised database administrator. No public platform-operator self-enrolment route exists.

Before tenant-owned data access:
- Authenticate the caller.
- Resolve allowed tenant memberships from a trusted identity source.
- Verify the selected tenant is permitted for the caller and operation.
- Enforce tenant scope in every persistence path.
- Include tenant context in audit records.
- Propagate trusted tenant context to messages and background jobs.
- Test cross-tenant reads and writes explicitly.

Never derive tenant authorization solely from a client-supplied header, query string or route value.
