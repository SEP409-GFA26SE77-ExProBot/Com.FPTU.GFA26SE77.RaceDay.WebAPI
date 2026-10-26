<#
.SYNOPSIS
  Clear Docker DB + seed RaceDay demo (1 lenh).

.EXAMPLE
  .\deploy\clear-and-seed.ps1
  .\deploy\clear-and-seed.ps1 -SkipMongo
#>

[CmdletBinding()]
param(
    [switch]$SkipMongo
)

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
    throw "Missing $SqlFile - run: python deploy/sql/generate_seed_demo.py"
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

$mongo = $null
if (-not $SkipMongo) {
    $mongo = Resolve-ComposeService "mongodb" "mongo:7"
    if ($mongo) { Write-Host "    mongodb:  $mongo" }
    else { Write-Host "    mongodb:  (skip)" -ForegroundColor DarkYellow }
}

Write-Step "Wait pg_isready"
$ok = $false
for ($i = 0; $i -lt 30; $i++) {
    docker exec $pg pg_isready -U postgres -d postgres 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $ok = $true; break }
    Start-Sleep -Seconds 1
}
if (-not $ok) { throw "Postgres not ready: $pg" }

Write-Step "Apply clear_and_seed.sql"
$bytes = [IO.File]::ReadAllBytes($SqlFile)
if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    $bytes = $bytes[3..($bytes.Length - 1)]
}
[Text.Encoding]::UTF8.GetString($bytes) |
    docker exec -i $pg psql -U postgres -d postgres -v ON_ERROR_STOP=1 | Out-Host
if ($LASTEXITCODE -ne 0) { throw "psql failed (exit $LASTEXITCODE)" }

if ($mongo) {
    Write-Step "Clear Mongo audit_entries"
    docker exec $mongo mongosh --quiet --eval "db.getSiblingDB('raceday').audit_entries.deleteMany({})" | Out-Host
}

Write-Step "Done"
Write-Host ""
Write-Host 'Password: MatKhau@123 (email verified, no OTP)' -ForegroundColor Green
Write-Host '  admin / admin@raceday.vn'
Write-Host '  cuong.le / cuong.le@raceday.vn   (organizer)'
Write-Host '  an.nguyen .. uyen.mai           (13 runners, 11 with BIB on Grand Marathon)'
Write-Host ""
