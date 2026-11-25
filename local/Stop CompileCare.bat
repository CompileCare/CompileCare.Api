@echo off
cd /d "%~dp0.."
docker compose --env-file local/.env -f local/docker-compose.yml down
echo CompileCare containers stopped. Database volume was retained.
pause
