#!/usr/bin/env bash
set -euo pipefail

if [[ -n "${GITHUB_TOKEN:-}" ]]; then
      --username "${GITHUB_ACTOR:-github-actions}" \
    --password "$GITHUB_TOKEN" \
    --store-password-in-clear-text
fi

dotnet restore HeapCare.Api.slnx --configfile .ci-nuget.config
dotnet tool restore
dotnet build HeapCare.Api.slnx --configuration Release --no-restore
dotnet ef migrations has-pending-model-changes \
  --project src/HeapCare.Infrastructure \
  --startup-project src/HeapCare.Api \
  --context HeapCareDbContext
dotnet test HeapCare.Api.slnx --configuration Release --no-build
