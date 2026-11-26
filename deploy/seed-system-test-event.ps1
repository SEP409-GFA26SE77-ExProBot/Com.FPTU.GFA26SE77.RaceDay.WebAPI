<#
.SYNOPSIS
  Import or reschedule one RaceDay system-test event in local Docker Postgres.
.DESCRIPTION
  Run after the API has applied the database migrations. -ResetDatabase clears
  the RaceDay PostgreSQL tables and loads the full system-test dataset.
.PARAMETER StartAt
  ISO 8601 timestamp with an explicit offset (or Z). Defaults to tomorrow at the
  current time. The SQL file also accepts this value as the event_start variable.
.PARAMETER DurationMinutes
  Length of the test race. Defaults to 120 minutes.
.PARAMETER PostgresContainer
  Optional name of a local Docker Postgres container. Defaults to the postgres
  service in this project's docker-compose.yml.
.EXAMPLE
  .\deploy\seed-system-test-event.ps1 -StartAt '2026-11-18T06:30:00+07:00'
.EXAMPLE
  .\deploy\seed-system-test-event.ps1 -ResetDatabase
#>

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$StartAt,
    [ValidateRange(1, 1440)][int]$DurationMinutes,
    [switch]$ResetDatabase,
    [string]$PostgresContainer
)

$ErrorActionPreference = 'Stop'
$sqlDir = Join-Path $PSScriptRoot 'sql'
$entryFile = 'seed_system_test_event.sql'
$sqlFiles = @($entryFile)
if ($ResetDatabase) {
    $entryFile = 'clear_and_seed_system_test.sql'
    $sqlFiles += $entryFile
}

if (-not $PostgresContainer) {
    $composeFile = Join-Path $PSScriptRoot 'docker-compose.yml'
    $PostgresContainer = docker compose -f $composeFile ps -q postgres
    if ($LASTEXITCODE -ne 0 -or -not $PostgresContainer) {
        throw 'Compose Postgres is unavailable. Run docker compose -f deploy/docker-compose.yml up -d postgres.'
    }
}

docker exec $PostgresContainer pg_isready -U postgres -d postgres | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Postgres is not ready.' }

$psqlArgs = @('-X', '-U', 'postgres', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1')
if ($PSBoundParameters.ContainsKey('StartAt')) {
    $psqlArgs += @('-v', "event_start=$StartAt")
}
if ($PSBoundParameters.ContainsKey('DurationMinutes')) {
    $psqlArgs += @('-v', "duration_minutes=$DurationMinutes")
}

# Copy the UTF-8 files to a private directory so psql can resolve relative includes.
$remoteDir = '/tmp/raceday-system-test-' + [Guid]::NewGuid().ToString('N')
docker exec $PostgresContainer mkdir $remoteDir
if ($LASTEXITCODE -ne 0) { throw 'Could not create a temporary SQL directory.' }

try {
    foreach ($file in $sqlFiles) {
        docker cp (Join-Path $sqlDir $file) "${PostgresContainer}:${remoteDir}/${file}"
        if ($LASTEXITCODE -ne 0) { throw "Could not copy $file into Postgres." }
    }

    Write-Host "Applying $entryFile to postgres in container $PostgresContainer"
    docker exec -e PGCLIENTENCODING=UTF8 $PostgresContainer psql @psqlArgs -f "$remoteDir/$entryFile" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Seed failed (psql exit $LASTEXITCODE). See the PostgreSQL error above." }
}
finally {
    $remoteFiles = @($sqlFiles | ForEach-Object { "$remoteDir/$_" })
    docker exec $PostgresContainer rm -f @remoteFiles | Out-Null
    docker exec $PostgresContainer rmdir $remoteDir | Out-Null
}
