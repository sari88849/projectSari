<#
.SYNOPSIS
    Creates (or resets) ValidationSystemDb and runs the verification checks.
.DESCRIPTION
    Runs the numbered scripts in order with sqlcmd (Windows Authentication),
    stopping on the first error. Scripts only touch ValidationSystemDb.
    NOTE: re-running resets the sample data, metadata and saved validations.
    Requires SQL Server 2016 or later (verified on 2019 and on LocalDB 2016).
.EXAMPLE
    .\setup-database.ps1
    .\setup-database.ps1 -Server ".\SQLEXPRESS"
    .\setup-database.ps1 -Server "(localdb)\MSSQLLocalDB"
#>
param(
    [string]$Server = "."
)

# sqlcmd results are checked through $LASTEXITCODE. "Continue" keeps Windows PowerShell 5.1 from turning
# sqlcmd's stderr output into a terminating error before the friendly messages below can be shown.
$ErrorActionPreference = "Continue"

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Host "sqlcmd was not found on PATH. Install the SQL Server command-line tools and try again." -ForegroundColor Red
    exit 1
}

# Common flags:
#   -E  Windows authentication          -b  fail (non-zero exit) on any SQL error
#   -I  QUOTED_IDENTIFIER on             -f 65001  read the script files as UTF-8 (Hebrew text)
#   -C  trust the server certificate. Newer sqlcmd versions (ODBC 18 / go-sqlcmd) encrypt by default
#       and reject the self-signed certificate of a local SQL Server; older versions accept -C as well.
$sqlcmdArgs = @('-S', $Server, '-E', '-b', '-I', '-C', '-f', '65001')

# Fail early with a clear message if the server cannot be reached or is older than SQL Server 2016 (v13).
$major = & sqlcmd @sqlcmdArgs -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int);"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Cannot connect to SQL Server '$Server' with Windows authentication. Check the server name (-Server) and that the service is running." -ForegroundColor Red
    exit 1
}
if ([int]("$major".Trim()) -lt 13) {
    Write-Host "SQL Server '$Server' is version $major; SQL Server 2016 (13) or later is required." -ForegroundColor Red
    exit 1
}

$scripts = Get-ChildItem -Path $PSScriptRoot -Filter "*.sql" | Sort-Object Name
foreach ($script in $scripts) {
    Write-Host "==> $($script.Name)" -ForegroundColor Cyan
    & sqlcmd @sqlcmdArgs -i $script.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED in $($script.Name)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host "Database setup completed successfully." -ForegroundColor Green
