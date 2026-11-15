<#
.SYNOPSIS
    Creates (or resets) ValidationSystemDb and runs the verification checks.
.DESCRIPTION
    Runs the numbered scripts in order with sqlcmd (Windows Authentication),
    stopping on the first error. Scripts only touch ValidationSystemDb.
    NOTE: re-running resets the sample data, metadata and saved validations.
.EXAMPLE
    .\setup-database.ps1
    .\setup-database.ps1 -Server ".\SQLEXPRESS"
#>
param(
    [string]$Server = "."
)

$ErrorActionPreference = "Stop"
$scripts = Get-ChildItem -Path $PSScriptRoot -Filter "*.sql" | Sort-Object Name

foreach ($script in $scripts) {
    Write-Host "==> $($script.Name)" -ForegroundColor Cyan
    # -E Windows auth, -b fail on error, -f 65001 read files as UTF-8 (Hebrew text), -I quoted identifiers
    & sqlcmd -S $Server -E -b -I -f 65001 -i $script.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED in $($script.Name)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host "Database setup completed successfully." -ForegroundColor Green
