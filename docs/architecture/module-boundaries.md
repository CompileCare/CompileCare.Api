# Module boundaries

The initial backend is a modular monolith. Each capability owns its application logic, domain model, persistence contracts and public integration surface. Cross-module access uses explicit contracts.

## Initial capability candidates

Identity and access; tenancy and organization hierarchy; hospital management; patient management; appointments and queue; clinical records; billing; pharmacy; laboratory; inventory; audit.

These are capability boundaries, not a commitment to one deployable service or repository per capability.

## Rules

1. Do not share mutable domain entities between modules.
2. Keep module internals internal by default.
3. Expose explicit contracts for synchronous module interactions.
4. Use integration events for asynchronous workflows where eventual consistency is acceptable.
5. Avoid cross-module database writes.
6. Define tenant ownership for every tenant-owned record.
7. Extract a module only with a documented operational and business reason.

Before extraction, plan contracts, data ownership/migration, idempotency, retries, observability, failure behaviour, tenant propagation and operations ownership.
