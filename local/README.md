# CompileCare Local

One-command local development environment for CompileCare.Api + CompileCare.Web + PostgreSQL.

## Requirements

- macOS (primary) or Windows 10/11 with Docker Desktop installed and running.
- Git and GitHub CLI (`gh`) authenticated to the CompileCare organization so the private Web repository can be cloned.
- A development OIDC application configured with:
  - Authority/issuer URL reachable from both the browser and API container.
  - Audience `compilecare-api`.
  - Client ID `compilecare-web`.
  - Redirect URI `http://localhost:8080/auth/callback`.
  - Post-logout redirect URI `http://localhost:8080`.
  - API access scope required by your identity provider.
- A GitHub Personal Access Token with **read:packages** permission for the CompileCare GitHub Packages feed. It is stored only in the ignored local `.secrets` directory and passed to Docker BuildKit as a build secret.

Do not use production identity credentials or production patient data.

## macOS

Double-click `Start CompileCare.command`. On first run it checks prerequisites, clones the private Web repository next to this repository if needed, asks for the OIDC settings and GitHub Packages token, then builds and starts the stack.

If macOS blocks the downloaded command file, open Terminal in this folder and run:

```bash
chmod +x "Start CompileCare.command"
```

Then double-click it again.

## Windows

Double-click `Start CompileCare.bat`.

## URLs

- Web: http://localhost:8080
- API: http://localhost:8081
- API OpenAPI (Development): http://localhost:8081/openapi/v1.json

The API applies EF Core migrations automatically only when running in the Development environment and `Database__ApplyMigrationsOnStartup=true`. This setting is enabled only by the local Compose file; normal deployments do not run migrations automatically.

## Stop

Double-click `Stop CompileCare.command` (macOS) or `Stop CompileCare.bat` (Windows), or run:

```bash
docker compose --env-file local/.env -f local/docker-compose.yml down
```

Data is retained in the Docker volume. To permanently delete the local database, run `docker compose --env-file local/.env -f local/docker-compose.yml down --volumes` after confirming you no longer need the local data.

## Security notes

- Never commit `local/.env` or `local/.secrets/`.
- The GitHub Packages token is supplied as a BuildKit secret, not a Docker build argument or image layer.
- The database is bound to loopback and uses a local-only password.
- API authentication, issuer/audience validation, persisted membership checks and tenant authorization remain enabled.
- No automatic platform-operator bootstrap or fake login is created. Provision development identities through the documented, authorised bootstrap process.
