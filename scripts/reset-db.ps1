$ErrorActionPreference = "Stop"

$environment = $env:ASPNETCORE_ENVIRONMENT
if ([string]::IsNullOrWhiteSpace($environment)) {
    $environment = $env:DOTNET_ENVIRONMENT
}

if ($environment -ne "Development") {
    Write-Error "reset-db.ps1 is DEV ONLY. Set ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT to Development."
    exit 1
}

$server = if ($env:GCW_SQL_SERVER) { $env:GCW_SQL_SERVER } else { "localhost" }
$sqlcmd = "C:\Program Files\SqlCmd\sqlcmd.exe"
if (-not (Test-Path $sqlcmd)) {
    $sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
}

$databases = @(
    "AuthDB",
    "CustomerDB",
    "WasherDB",
    "CatalogDB",
    "BookingDB",
    "PaymentDB",
    "NotificationDB"
)

$batches = foreach ($name in $databases) {
@"
IF DB_ID(N'$name') IS NOT NULL
BEGIN
    ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$name];
END
GO
"@
}

$drop = ($batches -join "`r`n")
$dropFile = Join-Path $env:TEMP "greencarwash-reset-db.sql"
Set-Content -Path $dropFile -Value $drop -Encoding ascii

& $sqlcmd -S $server -E -C -b -i $dropFile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "setup-db.ps1")
exit $LASTEXITCODE
