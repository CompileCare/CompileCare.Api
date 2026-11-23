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

- .NET 10 / ASP.NET Core
- EF Core 10
- PostgreSQL via Npgsql
- Versioned platform endpoint
- OpenAPI in Development
- Shared `CompileCare.Observability` library for structured logging, OpenTelemetry traces/metrics and health checks
- Liveness/readiness endpoints provided by the shared observability package
- Correlation ID propagation through `X-Correlation-ID`
- Request-scoped tenant context through `X-Tenant-ID`
- Problem Details exception handling
- Dockerfile and GitHub Actions CI

## Database configuration

Set `ConnectionStrings__CompileCare` through environment variables or a secret store. Do not commit credentials.

Example local connection string:

```
Host=localhost;Database=compilecare;Username=compilecare;Password=replace-me
```

Infrastructure registers EF Core/Npgsql and database readiness only when the connection string is configured. Migrations are created with the EF Core CLI from the Infrastructure project.

```bash
dotnet ef migrations add <MigrationName> --project src/CompileCare.Infrastructure --startup-project src/CompileCare.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/CompileCare.Infrastructure --startup-project src/CompileCare.Api

dotnet ef migrations script --idempotent --project src/CompileCare.Infrastructure --startup-project src/CompileCare.Api --output artifacts/compilecare-migrations.sql
```

The design-time factory supports `ConnectionStrings__CompileCare`; its local fallback is tooling-only. Never use it in production. Production deployments must review and apply a generated migration script through the release process after a verified backup. **The API does not run migrations automatically at startup.** Keep migration files and the model snapshot in source control; never edit a migration already applied to a shared environment. Integration tests recreate their dedicated `_test` database and apply migrations rather than using `EnsureCreated`.

## Authentication and tenant authorization

Configure an OIDC identity provider using its HTTPS issuer/discovery endpoint and the API's registered audience. Do not commit credentials or signing keys.

Set the following environment variables in each deployed environment:

```text
Authentication__Authority=https://identity.example.com/
Authentication__Audience=compilecare-api
Authentication__RequireHttpsMetadata=true
```

The API validates JWT signature, issuer, audience, lifetime, expiry and subject. The validated issuer and subject (`sub`) are matched against an active CompileCare tenant membership. Facility endpoints require exactly one `X-Tenant-ID` GUID; this header only selects the requested tenant and never grants access by itself.

Tenant roles are stored in the CompileCare database. Current facility permissions are:
- `HospitalAdmin`: read, create/update and delete facility records.
- `FacilityManager`: read and create/update facility records.
- `FacilityViewer`: read facility records only.

Token-supplied role or permission claims do not grant CompileCare access. Membership provisioning and lifecycle management must be performed by a trusted administrative process; a self-service tenant onboarding or membership administration API is not yet included.

The integration suite uses synthetic, signed HS256 JWTs and a local static OIDC signing configuration. Its test key is not used by any deployed environment.

## Observability configuration

The API consumes stable shared packages from the CompileCare GitHub Packages NuGet feed. CI uses `GITHUB_TOKEN` with `packages: read`; grant the API repository read access in each package's Settings → Manage Actions access.

Set:
- `OpenTelemetry__ServiceName=CompileCare.Api`
- `OpenTelemetry__OtlpEndpoint=http://localhost:4318`
- `ElasticConfiguration__Uri` (optional)

Do not log patient data, credentials, tokens, or other sensitive healthcare information.

## Run

Requires the .NET 10 SDK and access to the CompileCare GitHub Packages feed.

```bash
dotnet restore CompileCare.Api.slnx
dotnet build CompileCare.Api.slnx
dotnet test CompileCare.Api.slnx
dotnet run --project src/CompileCare.Api
```

The integration suite exercises the facility API against PostgreSQL. CI provisions this service automatically. For local runs, provide `ConnectionStrings__CompileCareTests` pointing to a dedicated test database whose name ends in `_test` (the default is `compilecare_test`). The integration test host deletes and recreates that database, and refuses to reset a database without the required suffix. Do not point it at a development, staging, or production database.

Platform endpoints: `/`, `/health/live`, `/health/ready`, `/metrics`, `/api/v1/platform/context`, and (Development only) `/openapi/v1.json`.

## Facility hierarchy API

The facility module exposes CRUD for the hierarchy **Hospital → Department → Ward → Room → Bed**:

- Hospitals: `/api/v1/hospitals`
- Departments: `/api/v1/hospitals/{hospitalId}/departments`
- Wards: `/api/v1/departments/{departmentId}/wards`
- Rooms: `/api/v1/wards/{wardId}/rooms`
- Beds: `/api/v1/rooms/{roomId}/beds`

Each collection supports bounded `page` and `pageSize` pagination (default 50, maximum 100). See [the facility module documentation](docs/modules/facilities/README.md) for route behavior, resource constraints, and response metadata.

## Tenant isolation

The tenant header is request context only. It is **not authentication** and does not prove that a caller may access a tenant. Facility and platform-context API routes now require a validated OIDC/JWT principal and an active CompileCare membership matching the token issuer, subject and selected tenant. Persistence requires the validated tenant context for tenant-owned writes and applies tenant query filters. Background jobs and message consumers must establish an equivalent trusted tenant context before accessing tenant-owned data. The platform context endpoint is diagnostic only.

## Pull request workflow

Use a short-lived branch from `main`; open a focused PR; review the diff and passing CI; squash merge; delete the merged head branch and verify cleanup. No direct feature commits to `main`.
