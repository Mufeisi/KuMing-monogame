@echo off
cd /d "%~dp0.."
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "Tools\Mobile-BootstrapPackageRepoExport.ps1" -OutputRoot "Build\Server\MicroResources"
pause