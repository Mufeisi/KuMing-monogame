@echo off
cd /d "%~dp0.."
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "tools\Mobile-BootstrapPackageRepoExport.ps1" -OutputRoot "Build\Server\MicroResources"
pause