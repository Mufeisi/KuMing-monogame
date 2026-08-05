@echo off
cd /d "%~dp0..\.."
dotnet restore .\Client_MonoGame.Android\Client_MonoGame.Android.csproj -r android-arm64
if errorlevel 1 (
  echo 还原失败，停止构建。
  exit /b 1
)
dotnet publish .\Client_MonoGame.Android\Client_MonoGame.Android.csproj -f net10.0-android -c Release -r android-arm64 -p:MobileBootstrapAssetMode=Micro -p:AndroidPackageFormat=apk -p:ArchiveOnBuild=false -v:minimal
if errorlevel 1 (
  echo 构建失败。
  exit /b 1
)
echo 构建完成，已签名 APK （AOT）位于：
echo .\Client_MonoGame.Android\bin\Release\net10.0-android\android-arm64\publish
pause
