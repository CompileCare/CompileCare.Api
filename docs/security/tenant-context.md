# Tenant context and isolation

Tenant isolation is a mandatory platform invariant.

The current API foundation reads an optional `X-Tenant-ID` GUID into a request-scoped `ITenantContext`. Invalid values are rejected. This header is not an authorization claim and does not grant tenant access.

Before tenant-owned data access:
- Authenticate the caller.
- Resolve allowed tenant memberships from a trusted identity source.
- Verify the selected tenant is permitted for the caller and operation.
- Enforce tenant scope in every persistence path.
- Include tenant context in audit records.
- Propagate trusted tenant context to messages and background jobs.
- Test cross-tenant reads and writes explicitly.

Never derive tenant authorization solely from a client-supplied header, query string or route value.
