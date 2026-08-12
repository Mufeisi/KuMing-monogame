param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$OutputRoot = (Join-Path (Get-Location).Path "Build\\Mobile\\BootstrapRepo"),
    [string[]]$OnlyPackages = @(),
    [string]$ResourceVersion = ''
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Info([string]$Message) {
    Write-Host $Message
}

function Get-FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "路径不能为空。" }
    return [IO.Path]::GetFullPath($Path)
}

function Get-TrimmedFullPath([string]$Path) {
    return (Get-FullPath $Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathWithin([string]$Child, [string]$Parent) {
    $childPath = Get-TrimmedFullPath $Child
    $parentPath = Get-TrimmedFullPath $Parent
    return $childPath.Equals($parentPath, [StringComparison]::OrdinalIgnoreCase) -or
        $childPath.StartsWith($parentPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-PathWithin([string]$Child, [string]$Parent, [string]$Label) {
    if (-not (Test-PathWithin $Child $Parent)) {
        throw "$Label 越出允许根目录：$Child（根：$Parent）。"
    }
}

function Assert-NoReparsePath([string]$Path, [string]$Label, [switch]$AllowMissingLeaf) {
    $fullPath = Get-FullPath $Path
    $root = [IO.Path]::GetPathRoot($fullPath)
    $relative = $fullPath.Substring($root.Length).TrimStart([char[]]"\\/")
    $parts = if ($relative) { $relative.Split([char[]]"\\/") } else { @() }
    $current = $root
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $current = Join-Path $current $parts[$i]
        if (-not (Test-Path -LiteralPath $current)) {
            if ($AllowMissingLeaf -and $i -eq $parts.Count - 1) { break }
            continue
        }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label 含有 reparse point/symlink/junction：$current。"
        }
    }
}

function Assert-NoReparseDescendants([string]$Path, [string]$Label) {
    Assert-NoReparsePath $Path $Label
    foreach ($item in @(Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction Stop)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label 含有 reparse point/symlink/junction：$($item.FullName)。"
        }
    }
}

function Get-ContainedPath([string]$AllowedRoot, [string]$RelativePath, [string]$Label) {
    $root = Get-TrimmedFullPath $AllowedRoot
    $candidate = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    Assert-PathWithin $candidate $root $Label
    return $candidate
}

function Assert-SafeWindowsSegment([string]$Value, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -eq '.' -or $Value -eq '..') {
        throw "$Label 不能为空，且禁止 .、..：$Value。"
    }
    if ($Value.EndsWith('.') -or $Value.EndsWith(' ')) {
        throw "$Label 不能以点或空格结尾：$Value。"
    }
    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsControl($character) -or ([IO.Path]::GetInvalidFileNameChars() -contains $character)) {
            throw "$Label 含有 Windows 非法文件名字符：$Value。"
        }
    }
    $deviceName = $Value.TrimEnd([char[]]". ").Split('.')[0].ToUpperInvariant()
    if ($deviceName -in @('CON', 'PRN', 'AUX', 'NUL') -or
        $deviceName -match '^(COM|LPT)[1-9]$') {
        throw "$Label 使用 Windows 保留设备名：$Value。"
    }
}

function Assert-SafePackageName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "分包 Name 不能为空。"
    }
    if ([IO.Path]::IsPathRooted($Name) -or $Name.Contains('/') -or $Name.Contains('\')) {
        throw "分包 Name 必须是单个相对路径段：$Name。"
    }
    Assert-SafeWindowsSegment $Name '分包 Name'
    return $Name
}

function Normalize-AssetPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) {
        throw "Asset 路径必须是非空相对路径：$Path。"
    }
    $normalized = $Path.Replace('\', '/').Normalize([Text.NormalizationForm]::FormC)
    if ($normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:') {
        throw "Asset 路径必须是非 rooted 相对路径：$Path。"
    }
    $segments = $normalized.Split('/')
    foreach ($segment in $segments) {
        Assert-SafeWindowsSegment $segment 'Asset 路径段'
    }
    return ($segments -join '\')
}

function Get-Sha256Hex([string]$Text) {
    $digest = [Security.Cryptography.SHA256]::Create().ComputeHash($utf8NoBom.GetBytes($Text))
    return ([BitConverter]::ToString($digest) -replace '-', '').ToLowerInvariant()
}

function Sort-PackageRecords([object[]]$Packages) {
    $byName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    $names = [Collections.Generic.List[string]]::new()
    foreach ($package in $Packages) {
        $name = Assert-SafePackageName ([string]$package.Name)
        if (-not $byName.TryAdd($name, $package)) {
            throw "分包名称重复（OrdinalIgnoreCase）：$name。"
        }
        $names.Add($name) | Out-Null
    }
    $names.Sort([StringComparer]::Ordinal)
    $sorted = [Collections.Generic.List[object]]::new()
    foreach ($name in $names) {
        $sorted.Add($byName[$name]) | Out-Null
    }
    return $sorted.ToArray()
}

function Get-PackageSetDigest([object[]]$Packages) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($package in (Sort-PackageRecords $Packages)) {
        $lines.Add("$([string]$package.Name)|$([string]$package.Sha256)|$([Int64]$package.Size)") | Out-Null
    }
    return Get-Sha256Hex ((($lines -join [Environment]::NewLine) + [Environment]::NewLine))
}

function New-DeterministicZip([string]$SourceRoot, [string]$ZipPath) {
    Assert-NoReparseDescendants $SourceRoot 'ZIP 暂存源'
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $root = (Resolve-Path -LiteralPath $SourceRoot).Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $pathMap = [Collections.Generic.Dictionary[string, IO.FileInfo]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/').Normalize([Text.NormalizationForm]::FormC)
        if (-not $pathMap.TryAdd($relative, $file)) {
            throw "规范化后出现重复 ZIP 路径（Ordinal）：$relative。"
        }
        $paths.Add($relative) | Out-Null
    }
    $paths.Sort([StringComparer]::Ordinal)

    $stream = [IO.File]::Open($ZipPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $fixedTime = [DateTimeOffset]::new([DateTime]::new(1980, 1, 1, 0, 0, 0, [DateTimeKind]::Utc))
            foreach ($relative in $paths) {
                $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTime
                $sourceStream = [IO.File]::OpenRead($pathMap[$relative].FullName)
                try {
                    $entryStream = $entry.Open()
                    try { $sourceStream.CopyTo($entryStream) }
                    finally { $entryStream.Dispose() }
                }
                finally { $sourceStream.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

$repoRoot = Get-TrimmedFullPath ((Resolve-Path -LiteralPath $RepositoryRoot).Path)
Assert-NoReparsePath $repoRoot '仓库根'
$bootstrapRoot = Get-ContainedPath $repoRoot 'src\Clients\Client_MonoGame.Shared\BootstrapAssets' 'BootstrapAssets 根'
$sharedRoot = Get-ContainedPath $repoRoot 'Client_MonoGame.Shared' 'Shared 根'
Assert-NoReparsePath $bootstrapRoot 'BootstrapAssets 根'
Assert-NoReparsePath $sharedRoot 'Shared 根'
$manifestPath = Get-ContainedPath $bootstrapRoot 'bootstrap-packages.json' 'bootstrap-packages.json'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "未找到 bootstrap-packages.json：$manifestPath"
}

Write-Info "RepositoryRoot = $repoRoot"
Write-Info "OutputRoot     = $OutputRoot"

$manifest = (Get-Content -Encoding UTF8 $manifestPath | Out-String | ConvertFrom-Json)
if ($null -eq $manifest -or $null -eq $manifest.Packs) {
    throw "bootstrap-packages.json 解析失败或 Packs 为空。"
}

$manifestPacks = @($manifest.Packs)
$packageNames = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($pack in $manifestPacks) {
    if ($null -eq $pack) { throw 'Packs 含有空分包记录。' }
    $packName = Assert-SafePackageName ([string]$pack.Name)
    if (-not $packageNames.TryAdd($packName, $pack)) {
        throw "分包名称重复或输出路径冲突（OrdinalIgnoreCase）：$packName。"
    }
}

$outputRoot = Get-TrimmedFullPath $OutputRoot
Assert-NoReparsePath $outputRoot '输出根' -AllowMissingLeaf
$packagesOut = Get-ContainedPath $outputRoot 'Packages' 'Packages 输出根'
$tempRoot = Get-ContainedPath $outputRoot '_tmp' 'ZIP 暂存根'
$indexFileName = "bootstrap-package-index.json"

Assert-NoReparsePath $packagesOut 'Packages 输出根' -AllowMissingLeaf
New-Item -ItemType Directory -Force -Path $packagesOut | Out-Null

if (Test-Path -LiteralPath $tempRoot) {
    Assert-NoReparseDescendants $tempRoot 'ZIP 暂存根'
    Remove-Item -Recurse -Force $tempRoot
}
Assert-NoReparsePath $tempRoot 'ZIP 暂存根' -AllowMissingLeaf
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
Assert-NoReparsePath $tempRoot 'ZIP 暂存根'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$filter = @()
if ($OnlyPackages -and $OnlyPackages.Count -gt 0) {
    $filter = $OnlyPackages | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() }
    Write-Info ("OnlyPackages   = " + ($filter -join ", "))
}

$exported = 0
$skipped = 0
$indexPackages = New-Object System.Collections.Generic.List[object]

foreach ($pack in $manifestPacks) {
    $name = Assert-SafePackageName ([string]$pack.Name)

    if ($filter.Count -gt 0 -and -not ($filter -contains $name)) {
        $skipped++
        continue
    }

    if ($null -eq $pack.Assets -or $pack.Assets.Count -eq 0) {
        Write-Info "[SKIP] $name：Assets 为空。"
        $skipped++
        continue
    }

    $staging = Get-ContainedPath $tempRoot $name "分包 $name 暂存目录"
    $stagingPackagesRoot = Get-ContainedPath $staging "Packages\$name" "分包 $name 暂存输出"
    Assert-NoReparsePath $staging '分包暂存目录' -AllowMissingLeaf
    New-Item -ItemType Directory -Force -Path $stagingPackagesRoot | Out-Null
    Assert-NoReparsePath $stagingPackagesRoot '分包暂存输出'

    $missing = 0
    foreach ($asset in $pack.Assets) {
        $relative = Normalize-AssetPath ([string]$asset)

        $sourcePath = $null
        $bootstrapCandidate = Get-ContainedPath $bootstrapRoot $relative "分包 $name BootstrapAssets 源"
        Assert-NoReparsePath $bootstrapCandidate "分包 $name BootstrapAssets 源" -AllowMissingLeaf
        if (Test-Path -LiteralPath $bootstrapCandidate -PathType Leaf) {
            $sourcePath = $bootstrapCandidate
        }
        else {
            $sharedCandidate = Get-ContainedPath $sharedRoot $relative "分包 $name Shared 源"
            Assert-NoReparsePath $sharedCandidate "分包 $name Shared 源" -AllowMissingLeaf
            if (Test-Path -LiteralPath $sharedCandidate -PathType Leaf) {
                $sourcePath = $sharedCandidate
            }
        }

        if ([string]::IsNullOrWhiteSpace($sourcePath)) {
            $missing++
            continue
        }

        $destPath = Get-ContainedPath $stagingPackagesRoot $relative "分包 $name 暂存目标"
        $destDir = Split-Path -Parent $destPath
        Assert-NoReparsePath $destDir "分包 $name 暂存目标父目录" -AllowMissingLeaf
        if (-not (Test-Path -LiteralPath $destDir -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        }
        Assert-NoReparsePath $destPath "分包 $name 暂存目标" -AllowMissingLeaf

        Copy-Item -LiteralPath $sourcePath -Destination $destPath -Force
    }

    if ($missing -gt 0) {
        throw "分包 $name 导出失败：存在 $missing 个 Assets 在 BootstrapAssets 中缺失（请先修复资源一致性）。"
    }

    $zipPath = Get-ContainedPath $packagesOut "$name.zip" "分包 $name ZIP 输出"
    Assert-NoReparsePath $zipPath "分包 $name ZIP 输出" -AllowMissingLeaf

    New-DeterministicZip $staging $zipPath

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    $shaPath = Get-ContainedPath $packagesOut "$name.zip.sha256" "分包 $name SHA256 输出"
    Assert-NoReparsePath $shaPath "分包 $name SHA256 输出" -AllowMissingLeaf
    [System.IO.File]::WriteAllText($shaPath, $hash, $utf8NoBom)

    $zipSize = (Get-Item -LiteralPath $zipPath).Length
    $indexPackages.Add([PSCustomObject]@{
        Name   = $name
        Sha256 = $hash
        Size   = [Int64]$zipSize
    }) | Out-Null

    $exported++
    Write-Info "[OK] $name -> $zipPath"

    Remove-Item -Recurse -Force $staging
}

if (Test-Path -LiteralPath $tempRoot) {
    Assert-NoReparseDescendants $tempRoot 'ZIP 暂存根'
    Remove-Item -Recurse -Force $tempRoot
}

try {
    $sortedIndexPackages = Sort-PackageRecords $indexPackages
    $packageSetDigest = Get-PackageSetDigest $sortedIndexPackages
    $stableResourceVersion = if ([string]::IsNullOrWhiteSpace($ResourceVersion)) { "content-$packageSetDigest" } else { $ResourceVersion.Trim() }
    $index = [PSCustomObject]@{
        # Keep the existing string field/consumer shape while making the baseline reproducible.
        GeneratedAtUtc  = "1970-01-01T00:00:00.0000000Z"
        ResourceVersion = $stableResourceVersion
        Packages        = $sortedIndexPackages
    }

    $indexJson = ($index | ConvertTo-Json -Depth 6)

    $indexOutPath = Get-ContainedPath $packagesOut $indexFileName '补丁索引输出'
    Assert-NoReparsePath $indexOutPath '补丁索引输出' -AllowMissingLeaf
    [System.IO.File]::WriteAllText($indexOutPath, $indexJson, $utf8NoBom)
    Write-Info "[OK] Index -> $indexOutPath"

    $baselinePath = Get-ContainedPath $bootstrapRoot $indexFileName '基线索引输出'
    Assert-NoReparsePath $baselinePath '基线索引输出' -AllowMissingLeaf

    # 注意：OnlyPackages 用于“局部导出”时，输出仓库的 index 只能包含已导出的包，否则会引用缺失 zip。
    # 但 baseline index 是客户端用于预登录更新的“壳包基线”，不能因为局部导出而丢失其它包（尤其是 core-startup）。
    if ($filter.Count -gt 0 -and (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
        $baseline = $null
        try {
            $baseline = (Get-Content -Encoding UTF8 $baselinePath | Out-String | ConvertFrom-Json)
        }
        catch {
            $baseline = $null
        }

        $merged = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)

        foreach ($p in @($baseline.Packages)) {
            $n = Assert-SafePackageName ([string]$p.Name)
            if (-not $merged.TryAdd($n, $p)) {
                throw "基线索引分包名称重复或输出路径冲突（OrdinalIgnoreCase）：$n。"
            }
        }

        foreach ($p in @($index.Packages)) {
            $n = Assert-SafePackageName ([string]$p.Name)
            $merged[$n] = $p
        }

        $baselineIndex = [PSCustomObject]@{
            GeneratedAtUtc  = $index.GeneratedAtUtc
            ResourceVersion = $index.ResourceVersion
            Packages        = Sort-PackageRecords $merged.Values
        }

        $baselineJson = ($baselineIndex | ConvertTo-Json -Depth 6)
        [System.IO.File]::WriteAllText($baselinePath, $baselineJson, $utf8NoBom)
        Write-Info "[OK] BaselineIndex(merge) -> $baselinePath"
    }
    else {
        [System.IO.File]::WriteAllText($baselinePath, $indexJson, $utf8NoBom)
        Write-Info "[OK] BaselineIndex -> $baselinePath"
    }
}
catch {
    Write-Warning "写入 $indexFileName 失败：$($_.Exception.Message)"
}

Write-Info ""
Write-Info "完成：Exported=$exported, Skipped=$skipped"
Write-Info "输出目录：$OutputRoot"
