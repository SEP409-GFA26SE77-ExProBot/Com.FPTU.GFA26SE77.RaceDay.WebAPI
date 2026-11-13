<#
.SYNOPSIS
  Seed staff-checkpoint workspace demo vào Postgres local (Docker compose ưu tiên).

.EXAMPLE
  .\deploy\seed-staff-workspace.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$DeployDir = $PSScriptRoot
$ComposeFile = Join-Path $DeployDir "docker-compose.yml"
$SqlFile = Join-Path $DeployDir "sql\seed_staff_workspace.sql"
$Root = Split-Path $DeployDir -Parent
$EnvFile = Join-Path $Root ".env"

if (-not (Test-Path $SqlFile)) {
    throw "Missing $SqlFile"
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

Write-Host ""
Write-Host "==> Resolve postgres" -ForegroundColor Cyan
$pg = Resolve-ComposeService "postgres" "postgres:18-alpine"
if (-not $pg) {
    foreach ($c in @("deploy-postgres-1", "raceday-postgres-1", "postgres")) {
        if (docker ps --format "{{.Names}}" | Where-Object { $_ -eq $c }) { $pg = $c; break }
    }
}

if ($pg) {
    Write-Host "    docker: $pg"
    Write-Host ""
    Write-Host "==> Wait pg_isready" -ForegroundColor Cyan
    $ok = $false
    for ($i = 0; $i -lt 30; $i++) {
        docker exec $pg pg_isready -U postgres -d postgres 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ok = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ok) { throw "Postgres not ready: $pg" }

    Write-Host ""
    Write-Host "==> Apply seed_staff_workspace.sql (UTF-8 docker cp)" -ForegroundColor Cyan
    $remoteSql = "/tmp/raceday_seed_staff_workspace.sql"
    docker cp $SqlFile "${pg}:${remoteSql}"
    if ($LASTEXITCODE -ne 0) { throw "docker cp failed" }

    docker exec -e PGCLIENTENCODING=UTF8 $pg `
        psql -U postgres -d postgres -v ON_ERROR_STOP=1 -f $remoteSql | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "psql failed (exit $LASTEXITCODE)" }

    docker exec $pg rm -f $remoteSql 2>$null | Out-Null
    Write-Host ""
    Write-Host "==> Done (staff workspace seed)" -ForegroundColor Green
    return
}

# Fallback: host psql chỉ khi .env trỏ localhost:5433 (an toàn, không seed DB xa).
if (-not (Test-Path $EnvFile)) {
    throw "Postgres Docker không chạy và thiếu .env. Start: docker compose -f deploy/docker-compose.yml up -d postgres"
}

$line = Get-Content -LiteralPath $EnvFile -Encoding UTF8 |
    Where-Object { $_ -match '^ConnectionStrings__Postgres=' } |
    Select-Object -First 1
if (-not $line) { throw 'Thiếu ConnectionStrings__Postgres trong .env.' }

$connection = @{}
($line.Substring($line.IndexOf('=') + 1) -split ';') | ForEach-Object {
    $entry = $_ -split '=', 2
    if ($entry.Count -eq 2) { $connection[$entry[0].Trim().ToLowerInvariant()] = $entry[1] }
}
if ($connection['host'] -notin @('localhost', '127.0.0.1') -or $connection['port'] -ne '5433') {
    throw 'Không có Docker Postgres; host seed chỉ cho localhost:5433 (xem .env.example).'
}

$psql = (Get-Command psql -ErrorAction SilentlyContinue).Source
if (-not $psql) { throw 'Không tìm thấy psql trong PATH.' }

Write-Host "    host: $($connection['host']):$($connection['port'])"
$env:PGPASSWORD = $connection['password']
try {
    & $psql -X -v ON_ERROR_STOP=1 -h $connection['host'] -p $connection['port'] `
        -U $connection['username'] -d $connection['database'] -f $SqlFile
    if ($LASTEXITCODE -ne 0) { throw "Seed thất bại (psql exit $LASTEXITCODE)." }
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "==> Done (staff workspace seed)" -ForegroundColor Green
