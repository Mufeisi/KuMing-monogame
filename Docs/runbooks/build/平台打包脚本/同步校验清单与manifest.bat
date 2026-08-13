@echo off
cd /d "%~dp0..\..\..\.."
dotnet run -c Debug --project Tools/MobileBootstrapAudit -- --sync-manifest
