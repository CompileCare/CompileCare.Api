$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$Local = Join-Path $Root "local"
Set-Location $Root

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker Desktop is required." }
docker info *> $null
if ($LASTEXITCODE -ne 0) { throw "Start Docker Desktop and retry." }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "Install GitHub CLI and authenticate with gh auth login." }
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "Authenticate GitHub CLI with gh auth login." }

$Web = Join-Path (Split-Path $Root -Parent) "CompileCare.Web"
if (-not (Test-Path (Join-Path $Web ".git"))) {
  gh repo clone CompileCare/CompileCare.Web $Web
  if ($LASTEXITCODE -ne 0) { throw "Could not clone CompileCare.Web." }
}
$env:COMPILECARE_WEB_PATH = $Web

$EnvFile = Join-Path $Local ".env"
if (-not (Test-Path $EnvFile)) {
  Copy-Item (Join-Path $Local ".env.example") $EnvFile
  $authority = Read-Host "OIDC Authority/issuer URL"
  if ([string]::IsNullOrWhiteSpace($authority)) { throw "OIDC authority is required." }
  $audience = Read-Host "OIDC Audience [compilecare-api]"
  if ([string]::IsNullOrWhiteSpace($audience)) { $audience = "compilecare-api" }
  $client = Read-Host "OIDC Web Client ID [compilecare-web]"
  if ([string]::IsNullOrWhiteSpace($client)) { $client = "compilecare-web" }
  $scope = Read-Host "OIDC Scope [openid profile]"
  if ([string]::IsNullOrWhiteSpace($scope)) { $scope = "openid profile" }
  $content = Get-Content $EnvFile -Raw
  $content = $content -replace '(?m)^OIDC_AUTHORITY=.*$', "OIDC_AUTHORITY=$authority"
  $content = $content -replace '(?m)^OIDC_AUDIENCE=.*$', "OIDC_AUDIENCE=$audience"
  $content = $content -replace '(?m)^OIDC_CLIENT_ID=.*$', "OIDC_CLIENT_ID=$client"
  $content = $content -replace '(?m)^OIDC_SCOPE=.*$', "OIDC_SCOPE=$scope"
  Set-Content -Path $EnvFile -Value $content -NoNewline
}
$Secrets = Join-Path $Local ".secrets"
New-Item -ItemType Directory -Force -Path $Secrets | Out-Null
$CredentialFile = Join-Path $Secrets "nuget-source-credentials"
if (-not (Test-Path $CredentialFile)) {
  $username = Read-Host "GitHub username [Shatru-compile]"
  if ([string]::IsNullOrWhiteSpace($username)) { $username = "Shatru-compile" }
  $secure = Read-Host "GitHub Personal Access Token (read:packages)" -AsSecureString
  $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
  try { $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
  if ([string]::IsNullOrWhiteSpace($token)) { throw "GitHub Packages token is required." }
  Set-Content -Path $CredentialFile -Value "Username=$username;Password=$token;ValidAuthenticationTypes=Basic" -NoNewline
  $token = $null
}
docker compose --env-file $EnvFile -f (Join-Path $Local "docker-compose.yml") up --build -d
if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed. Inspect logs with docker compose --env-file local/.env -f local/docker-compose.yml logs" }
Write-Host "Waiting for CompileCare..."
for ($i=0; $i -lt 60; $i++) {
  try {
    Invoke-WebRequest http://localhost:8081/ -UseBasicParsing -TimeoutSec 2 | Out-Null
    Invoke-WebRequest http://localhost:8080/ -UseBasicParsing -TimeoutSec 2 | Out-Null
    Start-Process "http://localhost:8080"
    Write-Host "CompileCare is running."
    exit 0
  } catch { Start-Sleep -Seconds 2 }
}
docker compose --env-file $EnvFile -f (Join-Path $Local "docker-compose.yml") ps
throw "Services did not become ready. Inspect Docker Compose logs."
