#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOCAL="$ROOT/local"
cd "$ROOT"

fail() { echo "ERROR: $*" >&2; read -r -p "Press Enter to close..."; exit 1; }
command -v docker >/dev/null || fail "Docker Desktop is required. Install it from https://www.docker.com/products/docker-desktop/."
docker info >/dev/null 2>&1 || fail "Docker Desktop is installed but not running. Start Docker Desktop and retry."
command -v gh >/dev/null || fail "GitHub CLI is required to clone the private Web repository. Install with: brew install gh"
gh auth status >/dev/null 2>&1 || fail "Authenticate GitHub CLI first: gh auth login"

WEB="$ROOT/../CompileCare.Web"
if [[ ! -d "$WEB/.git" ]]; then
  echo "Cloning CompileCare.Web..."
  gh repo clone CompileCare/CompileCare.Web "$WEB" || fail "Could not clone CompileCare.Web. Check repository access."
fi
export COMPILECARE_WEB_PATH="$WEB"

if [[ ! -f "$LOCAL/.env" ]]; then
  cp "$LOCAL/.env.example" "$LOCAL/.env"
  echo "Created local/.env from the example."
  read -r -p "OIDC Authority/issuer URL: " authority
  [[ -n "$authority" ]] || fail "OIDC authority is required."
  read -r -p "OIDC Audience [compilecare-api]: " audience
  audience="${audience:-compilecare-api}"
  read -r -p "OIDC Web Client ID [compilecare-web]: " client
  client="${client:-compilecare-web}"
  read -r -p "OIDC Scope [openid profile]: " scope
  scope="${scope:-openid profile}"
  sed -i '' "s|^OIDC_AUTHORITY=.*|OIDC_AUTHORITY=$authority|" "$LOCAL/.env"
  sed -i '' "s|^OIDC_AUDIENCE=.*|OIDC_AUDIENCE=$audience|" "$LOCAL/.env"
  sed -i '' "s|^OIDC_CLIENT_ID=.*|OIDC_CLIENT_ID=$client|" "$LOCAL/.env"
  sed -i '' "s|^OIDC_SCOPE=.*|OIDC_SCOPE=$scope|" "$LOCAL/.env"
fi

mkdir -p "$LOCAL/.secrets"
CREDENTIALS="$LOCAL/.secrets/nuget-source-credentials"
if [[ ! -s "$CREDENTIALS" ]]; then
  echo "A GitHub token with read:packages permission is needed to restore private CompileCare NuGet packages."
  read -r -p "GitHub username [Shatru-compile]: " username
  username="${username:-Shatru-compile}"
  read -r -s -p "GitHub Personal Access Token: " token
  echo
  [[ -n "$token" ]] || fail "A GitHub Packages token is required."
  umask 077
  printf 'Username=%s;Password=%s;ValidAuthenticationTypes=Basic' "$username" "$token" > "$CREDENTIALS"
  unset token
fi
chmod 600 "$CREDENTIALS"

echo "Building and starting CompileCare..."
docker compose --env-file "$LOCAL/.env" -f "$LOCAL/docker-compose.yml" up --build -d || fail "Docker Compose failed. Inspect logs with: docker compose --env-file local/.env -f local/docker-compose.yml logs"
echo "Waiting for services..."
for i in {1..60}; do
  if curl -fsS http://localhost:8081/ >/dev/null 2>&1 && curl -fsS http://localhost:8080/ >/dev/null 2>&1; then
    echo "CompileCare is running."
    open http://localhost:8080
    read -r -p "Press Enter to close this window..."
    exit 0
  fi
  sleep 2
done
docker compose --env-file "$LOCAL/.env" -f "$LOCAL/docker-compose.yml" ps
fail "Services did not become ready in time. Inspect logs with: docker compose --env-file local/.env -f local/docker-compose.yml logs"
