#!/usr/bin/env bash
set -euo pipefail

dotnet restore HeapCare.Api.slnx --configfile .ci-nuget.config
dotnet tool restore --configfile .ci-nuget.config
dotnet build HeapCare.Api.slnx --configuration Release --no-restore
dotnet ef migrations has-pending-model-changes \
  --project src/HeapCare.Infrastructure \
  --startup-project src/HeapCare.Api \
  --context HeapCareDbContext
dotnet test HeapCare.Api.slnx --configuration Release --no-build
