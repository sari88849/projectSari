<#
.SYNOPSIS
    Starts the API, the Python AI mock and the Angular dev server, each in its own window.
.NOTES
    Run db\setup-database.ps1 once before the first start.
    From Explorer, double-click start-dev.cmd (double-clicking a .ps1 only opens it in Notepad).
#>
$root = Split-Path -Parent $PSScriptRoot

# A process left over from an earlier run would answer on these ports instead of the new one.
# A stale Angular server with no API behind it answers every /api call with HTTP 500.
$ports = @{ 5080 = 'API'; 5005 = 'AI mock'; 4200 = 'Angular' }
$busy = foreach ($port in $ports.Keys) {
    $conn = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($conn) {
        $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
        "  port $port ($($ports[$port])) is used by PID $($conn.OwningProcess) $($proc.ProcessName)"
    }
}
if ($busy) {
    Write-Host "Cannot start: some ports are already in use (probably an earlier run that is still open):" -ForegroundColor Red
    $busy | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
    Write-Host "Close those windows or stop the processes (Stop-Process -Id <PID>), then run this script again." -ForegroundColor Red
    exit 1
}

Start-Process powershell -ArgumentList "-NoExit", "-Command",
    "`$host.UI.RawUI.WindowTitle = 'Validation API :5080'; cd '$root\backend'; dotnet run --project src/ValidationSystem.Api --launch-profile http"

Start-Process powershell -ArgumentList "-NoExit", "-Command",
    "`$host.UI.RawUI.WindowTitle = 'AI mock :5005'; cd '$root\ai-mock'; python server.py --port 5005"

$frontend = "$root\frontend"
$install = if (Test-Path "$frontend\node_modules") { "" } else { "npm install; " }
Start-Process powershell -ArgumentList "-NoExit", "-Command",
    "`$host.UI.RawUI.WindowTitle = 'Angular :4200'; cd '$frontend'; ${install}npm start"

Write-Host "Starting... open http://localhost:4200 once the Angular window reports 'Local: http://localhost:4200/'." -ForegroundColor Green
Write-Host "The API window must show 'Now listening on: http://localhost:5080'." -ForegroundColor Green
