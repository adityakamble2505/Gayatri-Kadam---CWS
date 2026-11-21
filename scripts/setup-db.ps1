$ErrorActionPreference = "Stop"

$server = if ($env:GCW_SQL_SERVER) { $env:GCW_SQL_SERVER } else { "localhost" }
$sqlcmd = "C:\Program Files\SqlCmd\sqlcmd.exe"
if (-not (Test-Path $sqlcmd)) {
    $sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
}

$root = Split-Path $PSScriptRoot -Parent
$schema = Join-Path $root "Docs\schema.sql"
$seed = Join-Path $root "Docs\seed.sql"

& $sqlcmd -S $server -E -C -b -i $schema
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $sqlcmd -S $server -E -C -b -i $seed
exit $LASTEXITCODE
