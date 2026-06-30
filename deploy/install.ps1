# Advisory rollout installer (Windows / PowerShell). One command to build + start the whole stack.
#   Run from this folder:  .\install.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "-- Advisory rollout --------------------------------------------------"

# 1. Docker present?
try { docker version | Out-Null } catch {
  Write-Host "X Docker is not installed/running. Install Docker Desktop and start it: https://docs.docker.com/get-docker/" -ForegroundColor Red
  exit 1
}
try { docker compose version | Out-Null } catch {
  Write-Host "X 'docker compose' (v2) not available. Update Docker Desktop." -ForegroundColor Red
  exit 1
}

# 2. .env present?
if (-not (Test-Path ".env")) {
  Write-Host "-> No .env found - creating one from .env.example."
  Copy-Item ".env.example" ".env"
  Write-Host "X Edit .env now (set SQL_SA_PASSWORD at minimum), then re-run .\install.ps1" -ForegroundColor Yellow
  exit 1
}

# 3. Build + start.
Write-Host "-> Building images from source (first run takes a few minutes)..."
docker compose up --build -d

# Read ports from .env (default if absent).
function Get-EnvVal($key, $default) {
  $line = (Get-Content .env | Where-Object { $_ -match "^$key=" } | Select-Object -First 1)
  if ($line) { return ($line -split "=",2)[1].Trim() } else { return $default }
}
$apiPort     = Get-EnvVal "API_PORT" "5000"
$consolePort = Get-EnvVal "CONSOLE_PORT" "8088"
$nexusPort   = Get-EnvVal "NEXUS_PORT" "8081"

# 4. Wait for the API.
Write-Host "-> Waiting for the API to come up..."
$ok = $false
for ($i = 0; $i -lt 30; $i++) {
  try { Invoke-WebRequest -UseBasicParsing -TimeoutSec 4 "http://localhost:$apiPort/api/health" | Out-Null; $ok = $true; break } catch { Start-Sleep 4 }
}

Write-Host ""
Write-Host "-- Done --------------------------------------------------------------"
if ($ok) { Write-Host "OK Stack is up." -ForegroundColor Green }
else { Write-Host "! API didn't answer yet - Nexus can take 1-2 min on first boot. Check: docker compose logs -f api" -ForegroundColor Yellow }
Write-Host ""
Write-Host "  Console (curation team):  http://localhost:$consolePort"
Write-Host "  Nexus repo (developers):  http://localhost:$nexusPort"
Write-Host "  API health:               http://localhost:$apiPort/api/health"
Write-Host ""
Write-Host "  Next: open the console, then follow docs/manual/TUTORIAL-gate-your-first-package.md"
Write-Host "  Stop:  docker compose down      Logs: docker compose logs -f"
