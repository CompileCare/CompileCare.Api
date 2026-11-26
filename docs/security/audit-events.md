# Audit events and traceability

CompileCare records metadata-only audit events for tenant-scoped, user-scoped and platform-operator API requests. Audit records support accountability and incident investigation; they are not application logs and must never contain clinical note text, request/response bodies, credentials, query strings or patient payloads.

## Recorded metadata

Each event records:
- Validated tenant ID, when tenant membership resolution succeeded.
- Requested tenant ID as untrusted caller input, separately from the validated tenant ID.
- Validated identity issuer and subject when available.
- Endpoint action name, resource type, target resource ID, HTTP method and route template.
- Outcome (Succeeded, Denied, Rejected or Failed) and HTTP status.
- Correlation/trace ID and UTC occurrence timestamp.

The target resource ID is resolved from the final GUID route parameter. This captures named resource routes such as {encounterId}, {queueEntryId} and {patientId}, rather than relying only on a conventional {id} parameter. Parent route context such as a hospital ID is not mistaken for the more specific child resource when the route contains both.

## Transaction and failure behavior

For audited requests, the business operation and its success audit event share the same database transaction. The audit middleware owns that request transaction; application services participating in an audited request must use the current EF Core transaction and must not independently begin, commit, roll back or dispose a nested transaction. Services may own a transaction only when no ambient transaction exists.

The one-time platform-operator bootstrap uses a PostgreSQL transaction-scoped advisory lock before checking whether an operator already exists. This serializes concurrent bootstrap attempts while allowing the service to participate in the audit middleware's transaction. PostgreSQL releases the lock automatically when the owning transaction commits or rolls back.

If the audit record cannot be committed, the business transaction is rolled back and the API returns an audit-unavailable response rather than reporting an unrecorded success. Failed endpoint execution is rolled back and a separate failure audit attempt is made.

The integration suite includes a PostgreSQL failure-injection regression: it forces audit-event insertion to fail during patient registration and verifies that the API returns 503 and neither the patient nor hospital registration is committed. This protects the fail-closed transaction boundary against future middleware or service refactors.

Audit events are append-only. Application-level change tracking rejects updates/deletes, and PostgreSQL protections prevent direct update or truncate operations. The API now exposes a bounded tenant-wide audit query and CSV export through `GET /api/v1/audit/events` and `GET /api/v1/audit/events/export`. Both require the server-created `audit.read` membership permission and a validated tenant-wide organization scope; a hospital-scoped administrator receives 403. The current role mapping grants `audit.read` to `HospitalAdmin`, but the service still rejects memberships whose organization scope is not tenant-wide. No patient-facing or platform-wide cross-tenant audit browsing is exposed.

The query requires UTC start/end timestamps, caps the range at 90 days and page size at 100, and filters by allow-listed action, resource type and resource ID. CSV export caps the range at 31 days and result count at 5,000 rows. Exported fields remain audit metadata only. CSV cells are quoted and spreadsheet formula/control-character prefixes are neutralized. Each export request itself is recorded as `ExportAuditEvents`; because request auditing completes after the query is prepared, that record appears in a subsequent audit query. These limits are implementation safeguards, not a substitute for a hospital-approved audit-reader roster, retention/legal-hold policy, export monitoring and production governance.

## Privacy and security requirements

- Do not add request or response body capture to the audit pipeline.
- Do not persist query-string values or free-form clinical content.
- Do not treat caller-supplied tenant IDs as validated tenant context.
- Preserve actor issuer/subject from validated authentication and membership resolution.
- Enforce tenant and organization scope on the underlying business operation independently of audit metadata.
- Include correlation IDs so protected application logs and audit records can be correlated without copying sensitive payloads.
- Test success, denied, rejected and failed outcomes, cross-tenant/cross-hospital access, append-only enforcement and resource identifier capture.

Audit records are sensitive security data. Retention, export, review access, alerting and archival policies require explicit operational and legal approval before production deployment.


## Bounded audit query and export API

The current tenant-wide read/export implementation is intentionally narrower than an unrestricted audit console:

- The endpoint is protected by the server-derived `audit.read` membership permission and requires `OrganizationScopeContext.IsTenantWide` plus a validated tenant context.
- The SQL query applies the validated tenant predicate and time range before pagination; events from other tenants are not returned.
- Hospital/department-scoped memberships are denied rather than being given a partial or inferred audit view. The audit event schema does not yet contain trusted validated hospital/department scope attribution.
- List requests are limited to 90 days and 100 rows per page. CSV exports are limited to 31 days and 5,000 rows.
- Filters are exact action, resource type and resource ID. No request/response body, clinical note, patient name, contact detail, token, credential or query string is exported.
- CSV formula values are neutralized even when preceded by whitespace; leading tab/CR/LF controls are prefixed safely. The export endpoint is itself audited and visible in a subsequent query.

The current `HospitalAdmin` permission mapping plus tenant-wide scope is a provisional access model. Before production activation, the pilot hospital must approve named audit readers and their responsibilities; security/privacy/legal stakeholders must approve retention, archival, legal hold, export review, alerting and deletion controls. Do not infer that tenant-wide scope alone is an approved compliance role.
