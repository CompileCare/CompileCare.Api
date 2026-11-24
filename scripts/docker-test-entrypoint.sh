#!/usr/bin/env bash
set -euo pipefail

if [[ -n "${GITHUB_TOKEN:-}" ]]; then
  dotnet nuget update source github \
    --username "${GITHUB_ACTOR:-github-actions}" \
    --password "$GITHUB_TOKEN" \
    --store-password-in-clear-text
fi

dotnet restore CompileCare.Api.slnx
dotnet tool restore
dotnet build CompileCare.Api.slnx --configuration Release --no-restore
dotnet ef migrations has-pending-model-changes \
  --project src/CompileCare.Infrastructure \
  --startup-project src/CompileCare.Api \
  --context CompileCareDbContext
dotnet test CompileCare.Api.slnx --configuration Release --no-build
