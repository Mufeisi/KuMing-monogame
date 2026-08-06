@echo off
cd /d "%~dp0..\.."
dotnet restore .\Client_MonoGame.iOS\Client_MonoGame.iOS.csproj -r ios-arm64 -p:EnableIosTarget=true
if errorlevel 1 (
  echo 还原失败，停止构建。
  exit /b 1
)
dotnet publish .\Client_MonoGame.iOS\Client_MonoGame.iOS.csproj -f net10.0-ios -c Release -r ios-arm64 -p:EnableIosTarget=true -p:ArchiveOnBuild=true -p:BuildIpa=true -p:CodesignKey="Apple Distribution: 你的公司名 (TEAMID)" -p:CodesignProvision="你的 Provisioning Profile 名称" -p:IpaPackagePath=".\Build\iOS\Client_MonoGame.ipa" -v:minimal
if errorlevel 1 (
  echo 构建失败。
  exit /b 1
)
echo 构建完成，已签名 APK （AOT）位于：
echo .\Build\iOS\Client_MonoGame.ipa
pause
