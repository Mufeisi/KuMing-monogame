@echo off
cd /d "%~dp0.."
dotnet restore .\Client_MonoGame.Android\Client_MonoGame.Android.csproj -r android-arm64
dotnet publish .\Client_MonoGame.Android\Client_MonoGame.Android.csproj -f net11.0-android -c Release -r android-arm64 -p:MobileBootstrapAssetMode=Micro -p:AndroidPackageFormat=apk -p:ArchiveOnBuild=false -v:minimal
echo 构建完成，已签名 APK （AOT）位于：
echo .\Client_MonoGame.Android\bin\Release\net11.0-android\android-arm64\publish
pause
