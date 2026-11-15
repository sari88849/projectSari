<#
.SYNOPSIS
    Starts the API, the Python AI mock and the Angular dev server, each in its own window.
.NOTES
    Run db\setup-database.ps1 once before the first start.
#>
$root = Split-Path -Parent $PSScriptRoot

Start-Process powershell -ArgumentList "-NoExit", "-Command",
    "`$host.UI.RawUI.WindowTitle = 'Validation API :5080'; cd '$root\backend'; dotnet run --project src/ValidationSystem.Api --launch-profile http"

Start-Process powershell -ArgumentList "-NoExit", "-Command",
    "`$host.UI.RawUI.WindowTitle = 'AI mock :5005'; cd '$root\ai-mock'; python server.py --port 5005"

$frontend = "$root\frontend"
$install = if (Test-Path "$frontend\node_modules") { "" } else { "npm install; " }
Start-Process powershell -ArgumentList "-NoExit", "-Command",
    "`$host.UI.RawUI.WindowTitle = 'Angular :4200'; cd '$frontend'; ${install}npm start"

Write-Host "Starting... open http://localhost:4200 once the Angular window reports 'Local: http://localhost:4200/'." -ForegroundColor Green
