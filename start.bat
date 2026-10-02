@echo off
rem Thin wrapper: runs the PowerShell installer (no Git Bash needed). Arguments are passed through, e.g. start.bat -BuildLocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
pause
