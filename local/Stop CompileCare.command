#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
docker compose --env-file local/.env -f local/docker-compose.yml down
echo
echo "CompileCare containers stopped. Database volume was retained."
read -r -p "Press Enter to close..."
