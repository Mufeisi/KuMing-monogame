param(
    [ValidateSet('Prepare', 'Evaluate', 'Rollback')]
    [string]$Action = 'Prepare',
    [string]$RepositoryRoot = (Get-Location).Path,
    [Parameter(Mandatory = $true)]
    [string]$ChannelRoot,
    [string]$ReleaseId = '',
    [long]$Sequence = 0,
    [string]$MinimumClientVersion = '1.0.0',
    [string]$ResourceKeyId = 'resource-2026-a',
    [string]$ResourcePrivateKeyPath = 'Configs/ReleaseSecrets/resource-2026-a.pkcs8.dpapi',
    [string]$AndroidKeyStorePath = 'Configs/ReleaseSecrets/lyocrystal-android-2026-r2.keystore',
    [string]$AndroidPasswordPath = 'Configs/ReleaseSecrets/lyocrystal-android-2026-r2-password.dpapi',
    [string]$AndroidKeyPurpose = 'android-apk-2026',
    [string]$AndroidKeyAlias = 'lyocrystal-release-2026',
    [string]$JavaHome = '',
    [string]$MetricsPath = ''
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = [Text.UTF8Encoding]::new($false, $true)
$stateFormat = 'lyocrystal-release-channel-v1'
$metricsFormat = 'lyocrystal-release-metrics-v1'
$minimumSample = 100
$maximumUpdateFailureRate = 0.02
$maximumCrashRate = 0.01
$maximumConsecutiveFatalCrashes = 3

function Get-NormalizedDirectory([string]$Path, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Label 不能为空。" }
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { return $full }
    return $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-ReleaseId([string]$Value) {
    if ($Value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
        throw "ReleaseId 仅允许 1～64 位字母、数字、点、下划线和短横线。"
    }
}

function Assert-PathWithin([string]$Path, [string]$Root, [string]$Label) {
    $full = [IO.Path]::GetFullPath($Path)
    $normalizedRoot = Get-NormalizedDirectory $Root '允许根目录'
    if (-not $full.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) -and
        -not $full.StartsWith($normalizedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label 越出允许根目录：$full。"
    }
    return $full
}

function Write-AtomicJson([string]$Path, [object]$Value) {
    $directory = Split-Path -Parent $Path
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $partial = "$Path.partial-$([Guid]::NewGuid().ToString('N'))"
    try {
        $bytes = $utf8NoBom.GetBytes((ConvertTo-Json $Value -Depth 12) + "`n")
        $stream = [IO.FileStream]::new($partial, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }
        if ([IO.File]::Exists($Path)) {
            $backup = "$Path.replace-backup-$([Guid]::NewGuid().ToString('N'))"
            try { [IO.File]::Replace($partial, $Path, $backup) }
            finally { if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) } }
        }
        else {
            [IO.File]::Move($partial, $Path)
        }
    }
    finally {
        if ([IO.File]::Exists($partial)) { [IO.File]::Delete($partial) }
    }
}

function Read-ChannelState([string]$Root) {
    $path = Join-Path $Root 'channel-state.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "灰度状态不存在：$path。" }
    $state = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.Format -ne $stateFormat -or [string]::IsNullOrWhiteSpace([string]$state.CurrentReleaseId)) {
        throw '灰度状态格式无效，已失败关闭。'
    }
    Assert-ReleaseId ([string]$state.CurrentReleaseId)
    if (-not [string]::IsNullOrWhiteSpace([string]$state.PreviousReleaseId)) { Assert-ReleaseId ([string]$state.PreviousReleaseId) }
    return $state
}

function Set-ChannelState([string]$Root, [object]$State) {
    Write-AtomicJson (Join-Path $Root 'channel-state.json') $State
}

function Invoke-Checked([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory) {
    Write-Host "[运行] $FilePath $($Arguments -join ' ')"
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) { throw "命令失败，退出码 $LASTEXITCODE：$FilePath。" }
    }
    finally { Pop-Location }
}

function Copy-DirectoryStrict([string]$Source, [string]$Target) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { throw "目录不存在：$Source。" }
    [IO.Directory]::CreateDirectory($Target) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Target -Recurse -Force
    }
}

function Get-FileProof([string]$Root) {
    $rootLength = (Get-NormalizedDirectory $Root '工件根').Length + 1
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            Path = $_.FullName.Substring($rootLength).Replace('\', '/')
            Size = [long]$_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}

function Invoke-Rollback([string]$Root, [string]$Reason) {
    $state = Read-ChannelState $Root
    if ([string]::IsNullOrWhiteSpace([string]$state.PreviousReleaseId)) {
        throw '没有上一可运行版本，拒绝伪报回滚成功。'
    }
    $previousDirectory = Assert-PathWithin (Join-Path (Join-Path $Root 'releases') ([string]$state.PreviousReleaseId)) $Root '上一版本目录'
    if (-not (Test-Path -LiteralPath $previousDirectory -PathType Container)) {
        throw "上一可运行版本工件不存在：$previousDirectory。"
    }
    $oldCurrent = [string]$state.CurrentReleaseId
    $state.CurrentReleaseId = [string]$state.PreviousReleaseId
    $state.PreviousReleaseId = $oldCurrent
    $state.RolloutPercent = 100
    $state.Status = 'RolledBack'
    $state.Reason = $Reason
    $state.UpdatedUtc = [DateTime]::UtcNow.ToString('O')
    Set-ChannelState $Root $state
    Write-Host "[回滚] Current=$($state.CurrentReleaseId)，Previous=$($state.PreviousReleaseId)，Reason=$Reason"
}

function Invoke-Evaluate([string]$Root, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Evaluate 必须提供存在的 MetricsPath。'
    }
    $state = Read-ChannelState $Root
    $metrics = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($metrics.Format -ne $metricsFormat -or $metrics.ReleaseId -ne $state.CurrentReleaseId) {
        throw '观测指标格式或 ReleaseId 与当前灰度版本不一致。'
    }
    $updateAttempts = [long]$metrics.UpdateAttempts
    $updateFailures = [long]$metrics.UpdateFailures
    $launches = [long]$metrics.Launches
    $crashes = [long]$metrics.Crashes
    $fatal = [long]$metrics.ConsecutiveFatalCrashes
    if ($updateAttempts -lt 0 -or $updateFailures -lt 0 -or $launches -lt 0 -or $crashes -lt 0 -or $fatal -lt 0 -or
        $updateFailures -gt $updateAttempts -or $crashes -gt $launches) {
        throw '观测指标计数无效。'
    }
    $updateRate = if ($updateAttempts -eq 0) { 0.0 } else { $updateFailures / [double]$updateAttempts }
    $crashRate = if ($launches -eq 0) { 0.0 } else { $crashes / [double]$launches }
    $reasons = [Collections.Generic.List[string]]::new()
    if ($fatal -ge $maximumConsecutiveFatalCrashes) { $reasons.Add("连续致命崩溃=$fatal") }
    if ($updateAttempts -ge $minimumSample -and $updateRate -gt $maximumUpdateFailureRate) { $reasons.Add(('更新失败率={0:P2}' -f $updateRate)) }
    if ($launches -ge $minimumSample -and $crashRate -gt $maximumCrashRate) { $reasons.Add(('启动崩溃率={0:P2}' -f $crashRate)) }
    if ($reasons.Count -gt 0) {
        Invoke-Rollback $Root ($reasons -join '；')
        return
    }
    $samplesComplete = $updateAttempts -ge $minimumSample -and $launches -ge $minimumSample
    $state.Status = if ($samplesComplete) { 'CanaryHealthy' } else { 'CanaryObserving' }
    $state.Reason = if ($samplesComplete) { '指标在阈值内' } else { '样本不足，保持当前灰度比例' }
    $state.UpdatedUtc = [DateTime]::UtcNow.ToString('O')
    Set-ChannelState $Root $state
    Write-Host "[保持] Release=$($state.CurrentReleaseId)，UpdateRate=$updateRate，CrashRate=$crashRate，$($state.Reason)"
}

function Invoke-Prepare {
    $repo = Get-NormalizedDirectory $RepositoryRoot '仓库根目录'
    $channel = Get-NormalizedDirectory $ChannelRoot '发布渠道根目录'
    if (-not (Test-Path -LiteralPath (Join-Path $repo 'global.json') -PathType Leaf)) { throw 'RepositoryRoot 不是 LyoCrystal 仓库。' }
    Assert-ReleaseId $ReleaseId
    if ($Sequence -le 0) { throw 'Sequence 必须为正整数。' }
    $parsedVersion = [Version]::new()
    if (-not [Version]::TryParse($MinimumClientVersion, [ref]$parsedVersion)) { throw 'MinimumClientVersion 无效。' }
    $MinimumClientVersion = $parsedVersion.ToString()
    [IO.Directory]::CreateDirectory($channel) | Out-Null
    $releasesRoot = Join-Path $channel 'releases'
    [IO.Directory]::CreateDirectory($releasesRoot) | Out-Null
    $releaseDirectory = Assert-PathWithin (Join-Path $releasesRoot $ReleaseId) $channel '版本输出目录'
    if (Test-Path -LiteralPath $releaseDirectory) { throw "版本输出已存在，拒绝覆盖：$releaseDirectory。" }
    $partial = "$releaseDirectory.partial-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($partial) | Out-Null
    $transcriptStarted = $false
    try {
        Start-Transcript -LiteralPath (Join-Path $partial 'release-run-transcript.txt') -Force | Out-Null
        $transcriptStarted = $true
        Invoke-Checked 'dotnet' @('test', 'Tests/Base05.Tests/Base05.Tests.csproj', '-c', 'Release') $repo
        Invoke-Checked 'dotnet' @('publish', 'Client_VorticeDX11/Client_VorticeDX11.csproj', '-c', 'Release', '-o', (Join-Path $partial 'pc')) $repo
        Invoke-Checked 'dotnet' @('publish', 'Server.MirForms/Server.csproj', '-c', 'Release', '-o', (Join-Path $partial 'server')) $repo
        Invoke-Checked 'dotnet' @('restore', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '--verbosity', 'minimal') $repo
        Invoke-Checked 'dotnet' @('build', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '-c', 'Release', '--no-restore') $repo
        Invoke-Checked 'dotnet' @('restore', 'Client_MonoGame.Android/Client_MonoGame.Android.csproj', '-r', 'android-arm64', '--verbosity', 'minimal') $repo
        $resourceRepo = Join-Path $partial 'resources'
        $baselineIndex = Join-Path $repo 'Client_MonoGame.Shared/BootstrapAssets/bootstrap-package-index.json'
        $baselineExisted = Test-Path -LiteralPath $baselineIndex -PathType Leaf
        $baselineBytes = if ($baselineExisted) { [IO.File]::ReadAllBytes($baselineIndex) } else { $null }
        try {
            Invoke-Checked 'pwsh' @('-NoProfile', '-File', (Join-Path $repo 'Tools/Mobile-BootstrapPackageRepoExport.ps1'), '-RepositoryRoot', $repo, '-OutputRoot', $resourceRepo) $repo
        }
        finally {
            if ($baselineExisted) { [IO.File]::WriteAllBytes($baselineIndex, $baselineBytes) }
            elseif (Test-Path -LiteralPath $baselineIndex -PathType Leaf) { Remove-Item -LiteralPath $baselineIndex -Force }
        }
        $unsignedIndex = Join-Path $resourceRepo 'packages/bootstrap-package-index.json'
        $signedIndex = Join-Path $resourceRepo 'packages/bootstrap-package-index.signed.json'
        Invoke-Checked 'dotnet' @('run', '--project', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '-c', 'Release', '--no-build', '--', 'sign-resource-index', $unsignedIndex, $signedIndex, $ResourceKeyId, $Sequence.ToString([Globalization.CultureInfo]::InvariantCulture), $MinimumClientVersion, (Join-Path $repo $ResourcePrivateKeyPath)) $repo
        Invoke-Checked 'dotnet' @('run', '--project', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '-c', 'Release', '--no-build', '--', 'verify-resource-index', $signedIndex, $MinimumClientVersion) $repo
        $androidLog = Join-Path $partial 'android-signing-build.log'
        Invoke-Checked 'dotnet' @('run', '--project', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '-c', 'Release', '--no-build', '--', 'publish-signed-android', 'Client_MonoGame.Android/Client_MonoGame.Android.csproj', (Join-Path $repo $AndroidKeyStorePath), (Join-Path $repo $AndroidPasswordPath), $AndroidKeyPurpose, $AndroidKeyAlias, $androidLog) $repo
        $apk = Get-ChildItem -LiteralPath (Join-Path $repo 'Client_MonoGame.Android/bin/Release/net10.0-android/android-arm64/publish') -Filter '*-Signed.apk' -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($null -eq $apk) { throw 'Android 签名构建未产生 Signed APK。' }
        $androidHome = if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
        if ([string]::IsNullOrWhiteSpace($androidHome)) { throw '未配置 ANDROID_HOME 或 ANDROID_SDK_ROOT，无法复验 APK 签名。' }
        $apkSigner = Get-ChildItem -LiteralPath $androidHome -File -Recurse |
            Where-Object { $_.Name -match '^apksigner\.(bat|cmd|exe)$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -eq $apkSigner) { throw 'Android SDK 中未找到 apksigner。' }
        $effectiveJavaHome = $JavaHome
        if ([string]::IsNullOrWhiteSpace($effectiveJavaHome)) { $effectiveJavaHome = $env:JAVA_HOME }
        if ([string]::IsNullOrWhiteSpace($effectiveJavaHome)) {
            $effectiveJavaHome = Get-ChildItem 'C:\Program Files\Android\openjdk' -Directory -ErrorAction SilentlyContinue |
                Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'bin\java.exe') -PathType Leaf } |
                Sort-Object Name -Descending |
                Select-Object -First 1 -ExpandProperty FullName
        }
        if ([string]::IsNullOrWhiteSpace($effectiveJavaHome) -or -not (Test-Path -LiteralPath (Join-Path $effectiveJavaHome 'bin\java.exe') -PathType Leaf)) {
            throw '未找到可用于 APK 验签的 JDK；请通过 JavaHome 提供 JDK 17 或更高版本。'
        }
        $previousJavaHome = $env:JAVA_HOME
        try {
            $env:JAVA_HOME = [IO.Path]::GetFullPath($effectiveJavaHome)
            Invoke-Checked $apkSigner.FullName @('verify', '--verbose', '--print-certs', $apk.FullName) $repo
        }
        finally { $env:JAVA_HOME = $previousJavaHome }
        Copy-Item -LiteralPath $apk.FullName -Destination (Join-Path $partial $apk.Name)
        Stop-Transcript | Out-Null
        $transcriptStarted = $false
        $descriptor = [ordered]@{
            Format = 'lyocrystal-release-artifact-v1'
            ReleaseId = $ReleaseId
            Sequence = $Sequence
            MinimumClientVersion = $MinimumClientVersion
            ResourceKeyId = $ResourceKeyId
            CreatedUtc = [DateTime]::UtcNow.ToString('O')
            Files = Get-FileProof $partial
        }
        Write-AtomicJson (Join-Path $partial 'release-manifest.json') $descriptor
        [IO.Directory]::Move($partial, $releaseDirectory)
        $previous = ''
        $statePath = Join-Path $channel 'channel-state.json'
        if (Test-Path -LiteralPath $statePath -PathType Leaf) { $previous = [string](Read-ChannelState $channel).CurrentReleaseId }
        $state = [ordered]@{
            Format = $stateFormat
            CurrentReleaseId = $ReleaseId
            PreviousReleaseId = $previous
            RolloutPercent = 5
            Status = 'Canary'
            Reason = '构建、冒烟、导出、签名与工件校验通过'
            UpdatedUtc = [DateTime]::UtcNow.ToString('O')
        }
        Set-ChannelState $channel $state
        Write-Host "[完成] RELEASE-02 一键发布已进入 5% 灰度：$ReleaseId。"
    }
    catch {
        if ($transcriptStarted) {
            try { Stop-Transcript | Out-Null } catch { }
        }
        if (Test-Path -LiteralPath $partial -PathType Container) { Remove-Item -LiteralPath $partial -Recurse -Force }
        throw
    }
}

$normalizedChannelRoot = Get-NormalizedDirectory $ChannelRoot '发布渠道根目录'
[IO.Directory]::CreateDirectory($normalizedChannelRoot) | Out-Null
$channelLockPath = Join-Path $normalizedChannelRoot 'release-channel.lock'
$channelLock = [IO.FileStream]::new($channelLockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    switch ($Action) {
        'Prepare' { Invoke-Prepare }
        'Evaluate' { Invoke-Evaluate $normalizedChannelRoot $MetricsPath }
        'Rollback' { Invoke-Rollback $normalizedChannelRoot '人工回滚' }
    }
}
finally { $channelLock.Dispose() }
