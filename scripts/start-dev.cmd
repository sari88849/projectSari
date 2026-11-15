@echo off
rem Double-clickable launcher. Windows opens .ps1 files in Notepad when double-clicked,
rem so this runs start-dev.ps1 with PowerShell. Bypass applies to this process only.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-dev.ps1"
pause
