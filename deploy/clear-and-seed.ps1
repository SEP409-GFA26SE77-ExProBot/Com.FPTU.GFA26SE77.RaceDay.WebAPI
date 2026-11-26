<#
.SYNOPSIS
  Clear Docker DB + seed RaceDay demo (1 lenh).

.EXAMPLE
  .\deploy\clear-and-seed.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$DeployDir = $PSScriptRoot
$ComposeFile = Join-Path $DeployDir "docker-compose.yml"
$SqlFile = Join-Path $DeployDir "sql\clear_and_seed.sql"

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Resolve-ComposeService([string]$service, [string]$ancestorFilter) {
    if (Test-Path $ComposeFile) {
        Push-Location $DeployDir
        try {
            $id = (docker compose -f $ComposeFile ps -q $service 2>$null | Select-Object -First 1)
            if ($id) {
                return (docker inspect -f '{{.Name}}' $id).TrimStart('/')
            }
        }
        finally { Pop-Location }
    }
    if ($ancestorFilter) {
        return docker ps --filter "ancestor=$ancestorFilter" --format "{{.Names}}" 2>$null | Select-Object -First 1
    }
    return $null
}

if (-not (Test-Path $SqlFile)) {
    throw "Missing $SqlFile"
}

Write-Step "Resolve containers"
$pg = Resolve-ComposeService "postgres" "postgres:18-alpine"
if (-not $pg) {
    foreach ($c in @("deploy-postgres-1", "raceday-postgres-1", "postgres")) {
        if (docker ps --format "{{.Names}}" | Where-Object { $_ -eq $c }) { $pg = $c; break }
    }
}
if (-not $pg) {
    throw "Postgres not found. Start: docker compose -f deploy/docker-compose.yml up -d postgres"
}
Write-Host "    postgres: $pg"

Write-Step "Wait pg_isready"
$ok = $false
for ($i = 0; $i -lt 30; $i++) {
    docker exec $pg pg_isready -U postgres -d postgres 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $ok = $true; break }
    Start-Sleep -Seconds 1
}
if (-not $ok) { throw "Postgres not ready: $pg" }

# Do NOT pipe SQL through PowerShell stdin (Windows re-encodes -> mojibake).
# Copy UTF-8 file into the container, then psql -f.
Write-Step "Apply clear_and_seed.sql (UTF-8 docker cp)"
$remoteSql = "/tmp/raceday_clear_and_seed.sql"
docker cp $SqlFile "${pg}:${remoteSql}"
if ($LASTEXITCODE -ne 0) { throw "docker cp failed" }

docker exec -e PGCLIENTENCODING=UTF8 $pg `
    psql -U postgres -d postgres -v ON_ERROR_STOP=1 -f $remoteSql | Out-Host
if ($LASTEXITCODE -ne 0) { throw "psql failed (exit $LASTEXITCODE)" }

docker exec $pg rm -f $remoteSql 2>$null | Out-Null

Write-Step "Done"
Write-Host ""
Write-Host 'Password ALL: MatKhau@123 (email verified, no OTP)' -ForegroundColor Green
Write-Host '  admin / admin@raceday.vn'
Write-Host '  manager / manager@raceday.vn'
Write-Host '  cuong.le / cuong.le@raceday.vn     (organizer Grand Marathon)'
Write-Host '  binh.tran                       (5KM Finished + certificate)'
Write-Host '  an.nguyen .. uyen.mai           (runners; 5KM published / 10KM sim pool)'
Write-Host '  coord1,coord2,vol1,vol2,photo1,photo2  (staff Invited on Grand)'
Write-Host 'Seed file: deploy/sql/clear_and_seed.sql (ONE FILE) — audit schema truncated with others'
Write-Host ""
