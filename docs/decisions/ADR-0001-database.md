# ADR-0001: PostgreSQL and EF Core

- Status: Accepted
- Date: 2026-11-23

## Context

HeapCare is a multi-tenant healthcare SaaS built as a .NET modular monolith. The platform needs relational integrity, migrations, tenant isolation, maintainability, and cost-effective deployment.

## Decision

Use PostgreSQL as the primary relational database and EF Core 10 as the default persistence technology, with the Npgsql provider.

Use EF Core for ordinary application persistence. Consider Dapper or SQL only for demonstrably performance-sensitive or complex read workloads, without making a second persistence stack the default.

## Consequences

- PostgreSQL is the supported database for development, CI and production.
- Schema changes are managed with EF Core migrations.
- Tenant-owned entities must derive from the persistence tenant base or implement the approved tenant ownership convention.
- Tenant authorization must be performed in the application; tenant query filters are defense-in-depth and not an authorization mechanism.
- PostgreSQL Row-Level Security may be added after tenant context propagation and pooled-connection handling are designed and tested.
- Tests should use PostgreSQL integration coverage rather than relying solely on EF's in-memory provider.
