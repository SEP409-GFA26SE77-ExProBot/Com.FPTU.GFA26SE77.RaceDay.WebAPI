@echo off
setlocal
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0clear-and-seed.ps1" %*
exit /b %ERRORLEVEL%
