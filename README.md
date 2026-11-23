# CompileCare.Api

Tenant-aware modular backend for the CompileCare healthcare platform.

## Architecture

The first delivery is a **modular monolith** with explicit capability boundaries. The target architecture is distributed, but modules will be extracted into independently deployable services only when justified by scaling, deployment, ownership, integration, or resilience needs.

## Solution layout

- `CompileCare.Api`: ASP.NET Core host, HTTP middleware and endpoints.
- `CompileCare.Application`: application contracts and use cases.
- `CompileCare.Infrastructure`: provider-specific adapters.
- `Modules/`: business capabilities added incrementally.
- `tests/`: unit and integration tests.

## Foundation

- .NET 9 / ASP.NET Core
- Versioned platform endpoint
- OpenAPI in Development
- Shared `CompileCare.Observability` library for structured logging, OpenTelemetry traces/metrics and health checks
- Liveness/readiness endpoints provided by the shared observability package
- Correlation ID propagation through `X-Correlation-ID`
- Request-scoped tenant context through `X-Tenant-ID`
- Problem Details exception handling
- Dockerfile and GitHub Actions CI

## Observability configuration

The API consumes the stable shared packages from the CompileCare GitHub Packages NuGet feed. CI requires package read access for the repository's `GITHUB_TOKEN`.

Set these configuration values (environment variables use the standard ASP.NET Core double-underscore convention):

- `OpenTelemetry__ServiceName=CompileCare.Api`
- `OpenTelemetry__OtlpEndpoint=http://localhost:4318`
- `ElasticConfiguration__Uri` (optional; Elasticsearch logging is enabled when configured)

Example:

```bash
OpenTelemetry__ServiceName=CompileCare.Api
OpenTelemetry__OtlpEndpoint=http://localhost:4317
```

The API enriches request logs with correlation ID and tenant ID when available. Do not log patient data, credentials, tokens, or other sensitive healthcare information.

## Run

Requires the .NET 9 SDK and access to the CompileCare GitHub Packages feed for the shared observability packages.

```bash
dotnet restore CompileCare.Api.slnx
dotnet build CompileCare.Api.slnx
dotnet test CompileCare.Api.slnx
dotnet run --project src/CompileCare.Api
```

Endpoints: `/`, `/health/live`, `/health/ready`, `/metrics`, `/api/v1/platform/context`, and (Development only) `/openapi/v1.json`.

## Tenant isolation

The tenant header is request context only. It is **not authentication** and does not prove that a caller may access a tenant. Before tenant-owned data is exposed, tenant identity must be bound to an authenticated principal and authorized against the requested resource. Tenant scope must be enforced in persistence, audit, background jobs and message consumers. The platform context endpoint is diagnostic only.

## Decisions intentionally left open

Database engine, identity provider, messaging broker, API gateway and deployment platform remain subject to approved architecture decision records. Do not add provider-specific runtime dependencies until those decisions are made.

## Pull request workflow

Use a short-lived branch from `main`; open a focused PR; review the diff and passing CI; squash merge; delete the merged head branch and verify cleanup. No direct feature commits to `main`.