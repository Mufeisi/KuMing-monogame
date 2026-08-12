param(
    [ValidateSet('Prepare', 'Evaluate', 'Rollback', 'Select', 'Record', 'Serve', 'StartGateway')]
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
    [string]$MetricsPath = '',
    [string]$ClientId = '',
    [ValidateSet('UpdateAttempt', 'UpdateFailure', 'Launch', 'Crash', 'FatalCrash', 'HealthyLaunch')]
    [string]$EventType = 'Launch',
    [string]$EventId = '',
    [string]$EventReleaseId = '',
    [string]$GatewayPrefix = 'http://127.0.0.1:18443/',
    [bool]$StartGateway = $true
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$utf8NoBom = [Text.UTF8Encoding]::new($false, $true)
$stateFormat = 'lyocrystal-release-channel-v1'
$metricsFormat = 'lyocrystal-release-metrics-v1'
$minimumSample = 100
$maximumUpdateFailureRate = 0.02
$maximumCrashRate = 0.01
$maximumConsecutiveFatalCrashes = 3
$validatedArtifacts = @{}
$collectorTokenEntropy = [Text.Encoding]::UTF8.GetBytes('LyoCrystal.Release02.CollectorToken.v1')
$maximumEventBodyBytes = 4096L

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
            [IO.File]::Replace($partial, $Path, $backup)
            if ([IO.File]::Exists($backup)) {
                try { [IO.File]::Delete($backup) }
                catch { Write-Warning "状态已经原子提交，但旧状态备份清理失败：$backup。" }
            }
        }
        else {
            [IO.File]::Move($partial, $Path)
        }
    }
    finally {
        if ([IO.File]::Exists($partial)) { [IO.File]::Delete($partial) }
    }
}

function Write-AtomicBytes([string]$Path, [byte[]]$Bytes) {
    $directory = Split-Path -Parent $Path
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $partial = "$Path.partial-$([Guid]::NewGuid().ToString('N'))"
    try {
        $stream = [IO.FileStream]::new($partial, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([IO.File]::Exists($Path)) { throw "受保护采集令牌已经存在，拒绝覆盖：$Path。" }
        [IO.File]::Move($partial, $Path)
    }
    finally { if ([IO.File]::Exists($partial)) { [IO.File]::Delete($partial) } }
}

function Get-OrCreateCollectorToken([string]$Root) {
    $path = Join-Path $Root 'release-events-token.dpapi'
    if (-not [IO.File]::Exists($path)) {
        $random = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($random) } finally { $rng.Dispose() }
        $plainText = [Convert]::ToBase64String($random)
        $plain = [Text.Encoding]::UTF8.GetBytes($plainText)
        try {
            $protected = [Security.Cryptography.ProtectedData]::Protect(
                $plain, $collectorTokenEntropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
            Write-AtomicBytes $path $protected
        }
        finally {
            [Array]::Clear($plain, 0, $plain.Length)
            [Array]::Clear($random, 0, $random.Length)
            $plainText = $null
        }
    }
    $protectedBytes = [IO.File]::ReadAllBytes($path)
    $unprotected = [Security.Cryptography.ProtectedData]::Unprotect(
        $protectedBytes, $collectorTokenEntropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { return [Text.Encoding]::UTF8.GetString($unprotected) }
    finally { [Array]::Clear($unprotected, 0, $unprotected.Length) }
}

function Test-FixedTimeToken([string]$Actual, [string]$Expected) {
    if ($null -eq $Actual -or $null -eq $Expected) { return $false }
    $actualBytes = [Text.Encoding]::UTF8.GetBytes($Actual)
    $expectedBytes = [Text.Encoding]::UTF8.GetBytes($Expected)
    try {
        if ($actualBytes.Length -ne $expectedBytes.Length) { return $false }
        $difference = 0
        for ($i = 0; $i -lt $actualBytes.Length; $i++) { $difference = $difference -bor ($actualBytes[$i] -bxor $expectedBytes[$i]) }
        return $difference -eq 0
    }
    finally {
        [Array]::Clear($actualBytes, 0, $actualBytes.Length)
        [Array]::Clear($expectedBytes, 0, $expectedBytes.Length)
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

function Get-Sha256Hex([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}

function Get-FileProof([string]$Root) {
    $rootLength = (Get-NormalizedDirectory $Root '工件根').Length + 1
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            Path = $_.FullName.Substring($rootLength).Replace('\', '/')
            Size = [long]$_.Length
            Sha256 = Get-Sha256Hex $_.FullName
        }
    })
}

function Assert-ReleaseArtifact([string]$Root, [string]$ExpectedReleaseId, [bool]$Force = $false) {
    $releaseDirectory = Assert-PathWithin (Join-Path (Join-Path $Root 'releases') $ExpectedReleaseId) $Root '发布版本目录'
    $manifestPath = Join-Path $releaseDirectory 'release-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "发布清单不存在：$manifestPath。" }
    $manifestInfo = Get-Item -LiteralPath $manifestPath
    $cacheKey = "$ExpectedReleaseId|$($manifestInfo.Length)|$($manifestInfo.LastWriteTimeUtc.Ticks)"
    if (-not $Force -and $validatedArtifacts.ContainsKey($cacheKey)) { return $releaseDirectory }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.Format -ne 'lyocrystal-release-artifact-v1' -or $manifest.ReleaseId -ne $ExpectedReleaseId) {
        throw '发布清单格式或 ReleaseId 不一致。'
    }
    $declared = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($manifest.Files)) {
        $relative = [string]$file.Path
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('..')) { throw '发布清单包含无效相对路径。' }
        if (-not $declared.Add($relative.Replace('\', '/'))) { throw "发布清单包含重复文件：$relative。" }
        $path = Assert-PathWithin (Join-Path $releaseDirectory ($relative.Replace('/', '\'))) $releaseDirectory '发布文件'
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "发布文件缺失：$relative。" }
        if ((Get-Item -LiteralPath $path).Length -ne [long]$file.Size -or
            (Get-Sha256Hex $path) -ne [string]$file.Sha256) {
            throw "发布文件完整性不匹配：$relative。"
        }
    }
    $actual = @(Get-ChildItem -LiteralPath $releaseDirectory -File -Recurse | ForEach-Object {
        $_.FullName.Substring($releaseDirectory.Length + 1).Replace('\', '/')
    } | Where-Object { $_ -ne 'release-manifest.json' })
    $extra = @($actual | Where-Object { -not $declared.Contains($_) })
    if ($extra.Count -gt 0) { throw "发布目录存在清单外文件：$($extra[0])。" }
    $signedIndex = Join-Path $releaseDirectory 'resources\Packages\bootstrap-package-index.signed.json'
    if (-not (Test-Path -LiteralPath $signedIndex -PathType Leaf)) { throw '发布版本缺少签名资源索引。' }
    $validatedArtifacts[$cacheKey] = $true
    return $releaseDirectory
}

function Select-ReleaseForClient([string]$Root, [string]$StableClientId) {
    if ([string]::IsNullOrWhiteSpace($StableClientId) -or $StableClientId.Length -gt 256) { throw 'ClientId 必须为 1～256 个字符。' }
    $state = Read-ChannelState $Root
    $selected = [string]$state.CurrentReleaseId
    if ([int]$state.RolloutPercent -lt 100 -and -not [string]::IsNullOrWhiteSpace([string]$state.PreviousReleaseId)) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($StableClientId)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = $sha.ComputeHash($bytes) } finally { $sha.Dispose() }
        $bucket = (([int]$hash[0] -shl 8) -bor [int]$hash[1]) % 100
        if ($bucket -ge [int]$state.RolloutPercent) { $selected = [string]$state.PreviousReleaseId }
    }
    Assert-ReleaseArtifact $Root $selected | Out-Null
    [ordered]@{ Format='lyocrystal-release-selection-v1'; ReleaseId=$selected; CurrentReleaseId=[string]$state.CurrentReleaseId; RolloutPercent=[int]$state.RolloutPercent; ArtifactBasePath="/releases/$selected/"; ResourceRepositoryPath="/releases/$selected/resources/" } |
        ConvertTo-Json -Compress
}

function Invoke-Rollback([string]$Root, [string]$Reason) {
    $state = Read-ChannelState $Root
    if ([string]$state.Status -eq 'RolledBack') { throw '当前版本已经回滚；没有新发布时拒绝重复回滚。' }
    if ([string]::IsNullOrWhiteSpace([string]$state.PreviousReleaseId)) {
        throw '没有上一可运行版本，拒绝伪报回滚成功。'
    }
    Assert-ReleaseArtifact $Root ([string]$state.PreviousReleaseId) $true | Out-Null
    $oldCurrent = [string]$state.CurrentReleaseId
    if ($null -eq $state.PSObject.Properties['FailedReleaseId']) {
        $state | Add-Member -NotePropertyName FailedReleaseId -NotePropertyValue ''
    }
    $state.CurrentReleaseId = [string]$state.PreviousReleaseId
    $state.PreviousReleaseId = ''
    $state.FailedReleaseId = $oldCurrent
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

function Record-ReleaseEvent([string]$Root, [string]$ObservedReleaseId, [string]$StableClientId, [string]$Type, [string]$Id) {
    if ($Id -notmatch '^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$') { throw 'EventId 格式无效。' }
    Assert-ReleaseId $ObservedReleaseId
    $state = Read-ChannelState $Root
    if ([string]$state.Status -eq 'RolledBack') { throw '当前灰度已经回滚，拒绝迟到事件。' }
    if (-not [string]::Equals($ObservedReleaseId, [string]$state.CurrentReleaseId, [StringComparison]::Ordinal)) {
        throw '事件 ReleaseId 不是当前灰度版本。'
    }
    $selection = Select-ReleaseForClient $Root $StableClientId | ConvertFrom-Json
    if (-not [string]::Equals([string]$selection.ReleaseId, $ObservedReleaseId, [StringComparison]::Ordinal)) {
        throw '事件客户端不属于当前灰度 cohort。'
    }
    $path = Join-Path $Root 'channel-metrics.json'
    $metrics = if (Test-Path -LiteralPath $path -PathType Leaf) {
        Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    } else { $null }
    if ($null -eq $metrics -or $metrics.Format -ne $metricsFormat -or $metrics.ReleaseId -ne $state.CurrentReleaseId) {
        $metrics = [pscustomobject][ordered]@{
            Format=$metricsFormat; ReleaseId=[string]$state.CurrentReleaseId
            UpdateAttempts=0L; UpdateFailures=0L; Launches=0L; Crashes=0L; ConsecutiveFatalCrashes=0L
            SeenEventIds=@(); UpdatedUtc=[DateTime]::UtcNow.ToString('O')
        }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($existing in @($metrics.SeenEventIds)) { [void]$seen.Add([string]$existing) }
    if (-not $seen.Add($Id)) { Write-Host "[忽略] 重复发布观测事件：$Id"; return }
    switch ($Type) {
        'UpdateAttempt' { $metrics.UpdateAttempts = [long]$metrics.UpdateAttempts + 1 }
        'UpdateFailure' { $metrics.UpdateAttempts = [long]$metrics.UpdateAttempts + 1; $metrics.UpdateFailures = [long]$metrics.UpdateFailures + 1 }
        'Launch' { $metrics.Launches = [long]$metrics.Launches + 1 }
        'Crash' { $metrics.Launches = [long]$metrics.Launches + 1; $metrics.Crashes = [long]$metrics.Crashes + 1 }
        'FatalCrash' { $metrics.Launches = [long]$metrics.Launches + 1; $metrics.Crashes = [long]$metrics.Crashes + 1; $metrics.ConsecutiveFatalCrashes = [long]$metrics.ConsecutiveFatalCrashes + 1 }
        'HealthyLaunch' { $metrics.Launches = [long]$metrics.Launches + 1; $metrics.ConsecutiveFatalCrashes = 0L }
    }
    $metrics.SeenEventIds = @($seen | Select-Object -Last 10000)
    $metrics.UpdatedUtc = [DateTime]::UtcNow.ToString('O')
    Write-AtomicJson $path $metrics
    Invoke-Evaluate $Root $path
}

function Assert-LoopbackGatewayPrefix([string]$Prefix) {
    $uri = [Uri]$Prefix
    if ($uri.Scheme -ne 'http' -or ($uri.Host -ne '127.0.0.1' -and $uri.Host -ne 'localhost')) {
        throw '发布网关只允许监听 loopback HTTP；对外发布必须由既有 TLS 反向代理转发。'
    }
    if (-not $Prefix.EndsWith('/')) { throw 'GatewayPrefix 必须以斜杠结尾。' }
}

function Invoke-WithChannelLock([string]$Root, [scriptblock]$Body) {
    $lockPath = Join-Path $Root 'release-channel.lock'
    $stream = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { & $Body } finally { $stream.Dispose() }
}

function Start-ReleaseGateway([string]$Root, [string]$Prefix) {
    Assert-LoopbackGatewayPrefix $Prefix
    $collectorToken = Get-OrCreateCollectorToken $Root
    $listener = [Net.HttpListener]::new()
    $listener.Prefixes.Add($Prefix)
    $listener.Start()
    Write-Host "[网关] 正在监听 $Prefix"
    try {
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            try {
                $path = $context.Request.Url.AbsolutePath.TrimEnd('/')
                if ($context.Request.HttpMethod -eq 'GET' -and $path -eq '/release/select') {
                    $id = [string]$context.Request.QueryString['clientId']
                    $body = Invoke-WithChannelLock $Root { Select-ReleaseForClient $Root $id }
                }
                elseif ($context.Request.HttpMethod -eq 'POST' -and $path -eq '/release/events') {
                    $authorization = [string]$context.Request.Headers['Authorization']
                    $suppliedToken = if ($authorization.StartsWith('Bearer ', [StringComparison]::Ordinal)) { $authorization.Substring(7) } else { '' }
                    if (-not (Test-FixedTimeToken $suppliedToken $collectorToken)) {
                        $context.Response.StatusCode = 401
                        throw '发布观测事件鉴权失败。'
                    }
                    if ($context.Request.ContentLength64 -lt 0) {
                        $context.Response.StatusCode = 411
                        throw '发布观测事件必须声明 Content-Length。'
                    }
                    if ($context.Request.ContentLength64 -eq 0 -or $context.Request.ContentLength64 -gt $maximumEventBodyBytes) {
                        $context.Response.StatusCode = 413
                        throw '发布观测事件正文必须为 1～4096 字节。'
                    }
                    if ($context.Request.InputStream.CanTimeout) { $context.Request.InputStream.ReadTimeout = 5000 }
                    $reader = [IO.StreamReader]::new($context.Request.InputStream, [Text.Encoding]::UTF8)
                    try { $event = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                    if ([string]$event.Format -ne 'lyocrystal-release-event-v1') { throw '发布观测事件格式无效。' }
                    $type = [string]$event.EventType
                    if ($type -notin @('UpdateAttempt','UpdateFailure','Launch','Crash','FatalCrash','HealthyLaunch')) { throw 'EventType 无效。' }
                    Invoke-WithChannelLock $Root {
                        Record-ReleaseEvent $Root ([string]$event.ReleaseId) ([string]$event.ClientId) $type ([string]$event.EventId)
                    }
                    $state = Read-ChannelState $Root
                    $body = [ordered]@{ Accepted=$true; CurrentReleaseId=[string]$state.CurrentReleaseId; Status=[string]$state.Status } | ConvertTo-Json -Compress
                }
                elseif ($context.Request.HttpMethod -eq 'GET' -and $path.StartsWith('/releases/', [StringComparison]::Ordinal)) {
                    $relative = [Uri]::UnescapeDataString($path.Substring('/releases/'.Length))
                    $separator = $relative.IndexOf('/')
                    if ($separator -le 0) { throw '发布文件路径缺少 ReleaseId。' }
                    $requestedRelease = $relative.Substring(0, $separator)
                    Assert-ReleaseId $requestedRelease
                    $releaseRoot = Assert-ReleaseArtifact $Root $requestedRelease
                    $fileRelative = $relative.Substring($separator + 1).Replace('/', [IO.Path]::DirectorySeparatorChar)
                    if ([string]::IsNullOrWhiteSpace($fileRelative) -or $fileRelative.Contains('..')) { throw '发布文件相对路径无效。' }
                    $filePath = Assert-PathWithin (Join-Path $releaseRoot $fileRelative) $releaseRoot '发布下载文件'
                    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) { $context.Response.StatusCode = 404; throw '发布文件不存在。' }
                    $info = Get-Item -LiteralPath $filePath
                    $context.Response.ContentType = 'application/octet-stream'
                    $start = 0L
                    $end = $info.Length - 1L
                    $range = [string]$context.Request.Headers['Range']
                    if (-not [string]::IsNullOrWhiteSpace($range)) {
                        $match = [Text.RegularExpressions.Regex]::Match($range, '^bytes=(\d+)-(\d*)$')
                        if (-not $match.Success) { $context.Response.StatusCode = 416; throw 'Range 格式无效。' }
                        if (-not [long]::TryParse($match.Groups[1].Value, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$start)) {
                            $context.Response.StatusCode = 416
                            throw 'Range 起点超出支持范围。'
                        }
                        if ($match.Groups[2].Success -and $match.Groups[2].Value.Length -gt 0) {
                            if (-not [long]::TryParse($match.Groups[2].Value, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$end)) {
                                $context.Response.StatusCode = 416
                                throw 'Range 终点超出支持范围。'
                            }
                        }
                        if ($start -ge $info.Length -or $end -lt $start -or $end -ge $info.Length) {
                            $context.Response.StatusCode = 416
                            $context.Response.AddHeader('Content-Range', "bytes */$($info.Length)")
                            throw 'Range 超出发布文件范围。'
                        }
                        $context.Response.StatusCode = 206
                        $context.Response.AddHeader('Accept-Ranges', 'bytes')
                        $context.Response.AddHeader('Content-Range', "bytes $start-$end/$($info.Length)")
                    }
                    $remaining = $end - $start + 1L
                    $context.Response.ContentLength64 = $remaining
                    $stream = [IO.File]::OpenRead($filePath)
                    try {
                        $stream.Position = $start
                        $buffer = [byte[]]::new(65536)
                        while ($remaining -gt 0) {
                            $read = $stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
                            if ($read -le 0) { throw '发布文件在 Range 传输期间意外结束。' }
                            $context.Response.OutputStream.Write($buffer, 0, $read)
                            $remaining -= $read
                        }
                    } finally { $stream.Dispose() }
                    $context.Response.Close()
                    continue
                }
                elseif ($context.Request.HttpMethod -eq 'GET' -and $path -eq '/health') {
                    $body = '{"status":"ok"}'
                }
                else { $context.Response.StatusCode = 404; $body = '{"error":"not_found"}' }
            }
            catch {
                if ($context.Response.StatusCode -lt 400) { $context.Response.StatusCode = 400 }
                $body = [ordered]@{ error=$_.Exception.Message } | ConvertTo-Json -Compress
            }
            $bytes = $utf8NoBom.GetBytes([string]$body)
            $context.Response.ContentType = 'application/json; charset=utf-8'
            $context.Response.ContentLength64 = $bytes.Length
            $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            $context.Response.Close()
        }
    }
    finally { $listener.Stop(); $listener.Close() }
}

function Ensure-ReleaseGatewayStarted([string]$Root) {
    Assert-LoopbackGatewayPrefix $GatewayPrefix
    $pidPath = Join-Path $Root 'release-gateway.pid'
    if (Test-Path -LiteralPath $pidPath -PathType Leaf) {
        try {
            $oldPid = [int](Get-Content -LiteralPath $pidPath -Raw -Encoding UTF8 | ConvertFrom-Json).ProcessId
            if (Get-Process -Id $oldPid -ErrorAction SilentlyContinue) { return }
        } catch { }
    }
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile','-ExecutionPolicy','Bypass','-File',$PSCommandPath,'-Action','Serve','-ChannelRoot',$Root,'-GatewayPrefix',$GatewayPrefix
    ) -WindowStyle Hidden -PassThru
    Write-AtomicJson $pidPath ([ordered]@{ ProcessId=$process.Id; Prefix=$GatewayPrefix; StartedUtc=[DateTime]::UtcNow.ToString('O') })
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
        Invoke-Checked 'dotnet' @('publish', 'src/Clients/Client_VorticeDX11/Client_VorticeDX11.csproj', '-c', 'Release', '-o', (Join-Path $partial 'pc')) $repo
        Invoke-Checked 'dotnet' @('publish', 'src/Server/Server.MirForms/Server.csproj', '-c', 'Release', '-o', (Join-Path $partial 'server')) $repo
        Invoke-Checked 'dotnet' @('restore', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '--verbosity', 'minimal') $repo
        Invoke-Checked 'dotnet' @('build', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '-c', 'Release', '--no-restore') $repo
        Invoke-Checked 'dotnet' @('restore', 'src/Clients/Client_MonoGame.Android/Client_MonoGame.Android.csproj', '-r', 'android-arm64', '--verbosity', 'minimal') $repo
        $resourceRepo = Join-Path $partial 'resources'
        $baselineIndex = Join-Path $repo 'src/Clients/Client_MonoGame.Shared/BootstrapAssets/bootstrap-package-index.json'
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
        Invoke-Checked 'dotnet' @('run', '--project', 'Tools/ReleaseSigningTool/ReleaseSigningTool.csproj', '-c', 'Release', '--no-build', '--', 'publish-signed-android', 'src/Clients/Client_MonoGame.Android/Client_MonoGame.Android.csproj', (Join-Path $repo $AndroidKeyStorePath), (Join-Path $repo $AndroidPasswordPath), $AndroidKeyPurpose, $AndroidKeyAlias, $androidLog) $repo
        $apk = Get-ChildItem -LiteralPath (Join-Path $repo 'src/Clients/Client_MonoGame.Android/bin/Release/net10.0-android/android-arm64/publish') -Filter '*-Signed.apk' -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
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
        if (Test-Path -LiteralPath $statePath -PathType Leaf) {
            $previous = [string](Read-ChannelState $channel).CurrentReleaseId
            Assert-ReleaseArtifact $channel $previous | Out-Null
        }
        $state = [ordered]@{
            Format = $stateFormat
            CurrentReleaseId = $ReleaseId
            PreviousReleaseId = $previous
            FailedReleaseId = ''
            RolloutPercent = 5
            Status = 'Canary'
            Reason = '构建、冒烟、导出、签名与工件校验通过'
            UpdatedUtc = [DateTime]::UtcNow.ToString('O')
        }
        Set-ChannelState $channel $state
        if ($StartGateway) { Ensure-ReleaseGatewayStarted $channel }
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
if ($Action -eq 'Serve') {
    Start-ReleaseGateway $normalizedChannelRoot $GatewayPrefix
}
else {
    Invoke-WithChannelLock $normalizedChannelRoot {
    switch ($Action) {
        'Prepare' { Invoke-Prepare }
        'Evaluate' { Invoke-Evaluate $normalizedChannelRoot $MetricsPath }
        'Rollback' { Invoke-Rollback $normalizedChannelRoot '人工回滚' }
        'Select' { Select-ReleaseForClient $normalizedChannelRoot $ClientId }
        'Record' { Record-ReleaseEvent $normalizedChannelRoot $EventReleaseId $ClientId $EventType $EventId }
        'StartGateway' { Ensure-ReleaseGatewayStarted $normalizedChannelRoot }
    }
    }
}
