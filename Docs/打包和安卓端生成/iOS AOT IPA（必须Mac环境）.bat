@echo off
cd /d "%~dp0.."
dotnet restore .\Client_MonoGame.iOS\Client_MonoGame.iOS.csproj -r ios-arm64
dotnet publish .\Client_MonoGame.iOS\Client_MonoGame.iOS.csproj -f net11.0-ios -c Release -r ios-arm64 -p:ArchiveOnBuild=true -p:BuildIpa=true -p:CodesignKey="Apple Distribution: 你的公司名 (TEAMID)" -p:CodesignProvision="你的 Provisioning Profile 名称" -p:IpaPackagePath=".\Build\iOS\Client_MonoGame.ipa" -v:minimal
echo 构建完成，已签名 APK （AOT）位于：
echo .\Build\iOS\Client_MonoGame.ipa
pause
