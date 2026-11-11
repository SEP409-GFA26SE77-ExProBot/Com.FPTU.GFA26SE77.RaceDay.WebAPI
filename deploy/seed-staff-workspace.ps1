# Seed a dedicated staff-workspace demo into the local Postgres only.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$envFile = Join-Path $root '.env'
$sqlFile = Join-Path $PSScriptRoot 'sql/seed_staff_workspace.sql'
$line = Get-Content -LiteralPath $envFile -Encoding UTF8 |
    Where-Object { $_ -match '^ConnectionStrings__Postgres=' } |
    Select-Object -First 1
if (-not $line) { throw 'Thiếu ConnectionStrings__Postgres trong .env.' }
$connection = @{}
($line.Substring($line.IndexOf('=') + 1) -split ';') | ForEach-Object {
    $entry = $_ -split '=', 2
    if ($entry.Count -eq 2) { $connection[$entry[0].Trim().ToLowerInvariant()] = $entry[1] }
}
if ($connection['host'] -notin @('localhost', '127.0.0.1') -or $connection['port'] -ne '5433') {
    throw 'Seed chỉ cho PostgreSQL local ở localhost:5433.'
}
$psql = (Get-Command psql -ErrorAction SilentlyContinue).Source
if (-not $psql) { throw 'Không tìm thấy psql trong PATH.' }
$env:PGPASSWORD = $connection['password']
try {
    & $psql -X -v ON_ERROR_STOP=1 -h $connection['host'] -p $connection['port'] `
        -U $connection['username'] -d $connection['database'] -f $sqlFile
    if ($LASTEXITCODE -ne 0) { throw "Seed thất bại (psql exit $LASTEXITCODE)." }
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
