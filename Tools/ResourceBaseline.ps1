param(
    [ValidateSet('Validate', 'Acquire')]
    [string]$Action = 'Validate',
    [ValidateSet('Repository', 'All')]
    [string]$Scope = 'Repository',
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$ManifestPath = '',
    [string]$ExternalRoot = ''
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$failures = [System.Collections.Generic.List[string]]::new()

function Get-FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '路径不能为空。' }
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

function Assert-RelativePath([string]$Value, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or [IO.Path]::IsPathRooted($Value)) {
        throw "$Label 必须是非空相对路径，拒绝绝对路径：$Value。"
    }
    $normalized = $Value.Replace('\', '/')
    if ($normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:') {
        throw "$Label 不是相对路径：$Value。"
    }
    foreach ($segment in $normalized.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -eq '.' -or $segment -eq '..') {
            throw "$Label 含有非法路径段（禁止 .、.. 和空段）：$Value。"
        }
    }
    return $normalized.Normalize([Text.NormalizationForm]::FormC)
}

function Assert-NoReparsePath([string]$Path, [string]$Label, [switch]$AllowMissingLeaf) {
    $fullPath = Get-FullPath $Path
    if (Test-Path -LiteralPath $fullPath) {
        $rootItem = Get-Item -LiteralPath $fullPath -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label 含有 reparse point/symlink/junction：$fullPath。"
        }
    }
    $root = [IO.Path]::GetPathRoot($fullPath)
    $relative = $fullPath.Substring($root.Length).TrimStart([char[]]"\\/")
    $current = $root
    $parts = if ($relative) { $relative.Split([char[]]"\\/") } else { @() }
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

function Add-Failure([string]$Message) {
    $failures.Add($Message) | Out-Null
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Get-TreeDigest([string]$Root, [string]$CanonicalPrefix) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return [PSCustomObject]@{ Exists = $false; FileCount = 0; Bytes = 0; Sha256 = '' }
    }
    Assert-NoReparseDescendants $Root "资源目录 $Root"
    $lines = [System.Collections.Generic.List[string]]::new()
    $files = @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring((Get-TrimmedFullPath $Root).Length + 1).Replace('\', '/')
        $canonicalPath = ($CanonicalPrefix.Trim([char[]]"\\/") + '/' + $relative).Normalize([Text.NormalizationForm]::FormC)
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
        $lines.Add("$canonicalPath|$($file.Length)|$hash") | Out-Null
    }
    $lines.Sort([StringComparer]::Ordinal)
    $canonical = (($lines -join "`n") + "`n")
    $digest = [Security.Cryptography.SHA256]::Create().ComputeHash($utf8NoBom.GetBytes($canonical))
    return [PSCustomObject]@{
        Exists = $true
        FileCount = $files.Count
        Bytes = [Int64](($files | Measure-Object -Property Length -Sum).Sum)
        Sha256 = ([BitConverter]::ToString($digest) -replace '-', '').ToLowerInvariant()
    }
}

function Get-ResourceTarget($Resource) {
    return Join-Path $repo ($Resource.path.Replace('/', [IO.Path]::DirectorySeparatorChar))
}

function Get-ResourceSource($Resource) {
    if ([string]::IsNullOrWhiteSpace($externalRoot)) { return $null }
    $relative = if ([string]::IsNullOrWhiteSpace([string]$Resource.sourcePath)) { $Resource.path } else { $Resource.sourcePath }
    $source = Join-Path $externalRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
    Assert-PathWithin $source $externalRoot "$($Resource.id) 外部源"
    if (Test-PathWithin $source $repo -or Test-PathWithin $repo $source) {
        throw "$($Resource.id) 外部源与仓库重叠，拒绝使用：$source。"
    }
    return $source
}

function Get-ResourcePhaseSpec($Resource, [ValidateSet('source', 'acquired', 'final')][string]$Phase) {
    $candidate = $null
    if ($Phase -eq 'source' -and $Resource.source -and $null -ne $Resource.source.sha256) {
        $candidate = $Resource.source
    }
    elseif ($Phase -eq 'acquired' -and $Resource.acquired) {
        $candidate = $Resource.acquired
    }
    elseif ($Phase -eq 'final' -and $Resource.final) {
        $candidate = $Resource.final
    }
    if ($null -ne $candidate) { return $candidate }
    return [PSCustomObject]@{
        fileCount = $Resource.fileCount
        bytes = $Resource.bytes
        sha256 = $Resource.sha256
        expected = $Resource.expected
    }
}

function Test-ExpectedFiles($Resource, [string]$Root, [string]$Label, $Expected = $null) {
    if ($null -eq $Expected) { $Expected = $Resource.expected }
    if ($null -eq $Expected) { return }
    $files = @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)
    foreach ($required in @($Expected.files | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })) {
        $relative = Assert-RelativePath ([string]$required) "$($Resource.id).expected.files"
        $candidate = Join-Path $Root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            Add-Failure "$Label：缺少必需文件 $relative。"
        }
    }
    foreach ($glob in @($Expected.globs | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })) {
        $pattern = Assert-RelativePath ([string]$glob) "$($Resource.id).expected.globs"
        $matched = @($files | Where-Object {
            $relative = $_.FullName.Substring((Get-TrimmedFullPath $Root).Length + 1).Replace('\', '/')
            $relative -like $pattern
        })
        if ($matched.Count -eq 0) {
            Add-Failure "$Label：未匹配到资源模式 $pattern。"
        }
    }
}

function Test-Resource($Resource, [string]$Root, [string]$Label, [switch]$RequireHash, [ValidateSet('source', 'acquired', 'final')][string]$Phase = 'final') {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        Add-Failure "$Label：目录不存在：$Root。"
        return $false
    }
    try {
        $failureCountBefore = $failures.Count
        Assert-NoReparseDescendants $Root $Label
        $spec = Get-ResourcePhaseSpec $Resource $Phase
        Test-ExpectedFiles $Resource $Root $Label $spec.expected
        $digest = Get-TreeDigest $Root ([string]$Resource.path)
        $expectedHash = [string]$spec.sha256
        $expectedFileCount = if ($null -ne $spec.fileCount) { [int]$spec.fileCount } else { $null }
        $expectedBytes = if ($null -ne $spec.bytes) { [Int64]$spec.bytes } else { $null }
        if ($null -ne $expectedFileCount -and ($digest.FileCount -ne $expectedFileCount -or $digest.Bytes -ne $expectedBytes)) {
            Add-Failure "$Label：文件计数/大小不匹配，期望 $expectedFileCount 个/$expectedBytes bytes，实际 $($digest.FileCount) 个/$($digest.Bytes) bytes。"
        }
        if ($RequireHash -and [string]::IsNullOrWhiteSpace($expectedHash)) {
            Add-Failure "$Label：SHA256 未固定；未知外部资源不能进入可复现基线。"
        }
        elseif (-not [string]::IsNullOrWhiteSpace($expectedHash) -and $digest.Sha256 -ne $expectedHash.ToLowerInvariant()) {
            Add-Failure "$Label：目录 SHA256 不匹配，期望 $expectedHash，实际 $($digest.Sha256)。"
        }
        if ($failures.Count -eq $failureCountBefore) {
            Write-Host "[OK] $Label：$($digest.FileCount) files, $($digest.Bytes) bytes, sha256=$($digest.Sha256)" -ForegroundColor Green
            return $true
        }
        return $false
    }
    catch {
        Add-Failure "$Label：$($_.Exception.Message)"
        return $false
    }
}

function Test-ResourceOverlays($Resource, [string]$Label) {
    if ($null -eq $Resource.overlays) { return }
    foreach ($overlay in @($Resource.overlays)) {
        $source = Join-Path $externalRoot ($overlay.sourcePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
        Assert-PathWithin $source $externalRoot "$Label overlay 源"
        if (Test-PathWithin $source $repo -or Test-PathWithin $repo $source) {
            Add-Failure "$Label overlay 源与仓库重叠：$source。"
            continue
        }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            Add-Failure "$Label overlay 源文件不存在：$source。"
            continue
        }
        try { Assert-NoReparsePath $source "$Label overlay 源" } catch { Add-Failure "$Label：$($_.Exception.Message)"; continue }
        $bytes = (Get-Item -LiteralPath $source -Force).Length
        if ($null -ne $overlay.bytes -and [Int64]$overlay.bytes -ne [Int64]$bytes) {
            Add-Failure "$Label overlay：文件大小不匹配，期望 $($overlay.bytes)，实际 $bytes。"
        }
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace([string]$overlay.sha256)) {
            Add-Failure "$Label overlay：SHA256 未固定。"
        }
        elseif ($hash -ne ([string]$overlay.sha256).ToLowerInvariant()) {
            Add-Failure "$Label overlay：SHA256 不匹配，期望 $($overlay.sha256)，实际 $hash。"
        }
    }
}

function Test-TargetEmpty([string]$Target, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Target)) { return $true }
    try { Assert-NoReparseDescendants $Target $Label } catch { Add-Failure "$Label：$($_.Exception.Message)"; return $false }
    if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
        Add-Failure "$Label：目标已存在且不是目录，拒绝覆盖：$Target。"
        return $false
    }
    if (@(Get-ChildItem -LiteralPath $Target -Force).Count -gt 0) {
        Add-Failure "$Label：目标非空，默认拒绝覆盖：$Target。"
        return $false
    }
    return $true
}

$repo = Get-TrimmedFullPath $RepositoryRoot
if (-not (Test-Path -LiteralPath $repo -PathType Container)) { throw "RepositoryRoot 不存在：$repo。" }
Assert-NoReparsePath $repo 'RepositoryRoot'

$manifestWasExplicit = -not [string]::IsNullOrWhiteSpace($ManifestPath)
if ($manifestWasExplicit) {
    $manifestRelative = Assert-RelativePath $ManifestPath 'ManifestPath'
    $manifestPath = Join-Path $repo ($manifestRelative.Replace('/', [IO.Path]::DirectorySeparatorChar))
}
else {
    $manifestPath = Join-Path $repo 'resources.manifest.json'
}
$manifestPath = Get-FullPath $manifestPath
Assert-PathWithin $manifestPath $repo 'ManifestPath'
Assert-NoReparsePath $manifestPath 'ManifestPath'

if (-not [string]::IsNullOrWhiteSpace($ExternalRoot)) {
    $externalRoot = Get-TrimmedFullPath $ExternalRoot
    if (-not (Test-Path -LiteralPath $externalRoot -PathType Container)) { throw "ExternalRoot 不存在：$externalRoot。" }
    if (Test-PathWithin $externalRoot $repo -or Test-PathWithin $repo $externalRoot) {
        throw "ExternalRoot 与 RepositoryRoot 重叠，拒绝使用：$externalRoot。"
    }
    Assert-NoReparseDescendants $externalRoot 'ExternalRoot'
}
else { $externalRoot = $null }

try {
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
}
catch { throw "无法读取资源清单：$manifestPath。$($_.Exception.Message)" }
if ($null -eq $manifest -or $null -eq $manifest.resources) { throw "资源清单无 resources：$manifestPath。" }

$resources = @($manifest.resources)
$targetPaths = @{}
foreach ($resource in $resources) {
    $resource.path = Assert-RelativePath ([string]$resource.path) "$($resource.id).path"
    if (-not [string]::IsNullOrWhiteSpace([string]$resource.sourcePath)) {
        $resource.sourcePath = Assert-RelativePath ([string]$resource.sourcePath) "$($resource.id).sourcePath"
    }
    if ($resource.expected) {
        $resource.expected.files = @($resource.expected.files | ForEach-Object { Assert-RelativePath ([string]$_) "$($resource.id).expected.files" })
        $resource.expected.globs = @($resource.expected.globs | ForEach-Object { Assert-RelativePath ([string]$_) "$($resource.id).expected.globs" })
    }
    foreach ($phaseName in @('source', 'acquired', 'final')) {
        $phaseSpec = $resource.PSObject.Properties[$phaseName].Value
        if ($phaseSpec -and $phaseSpec.expected) {
            $phaseSpec.expected.files = @($phaseSpec.expected.files | ForEach-Object { Assert-RelativePath ([string]$_) "$($resource.id).$phaseName.expected.files" })
            $phaseSpec.expected.globs = @($phaseSpec.expected.globs | ForEach-Object { Assert-RelativePath ([string]$_) "$($resource.id).$phaseName.expected.globs" })
        }
    }
    if ($resource.overlays) {
        foreach ($overlay in @($resource.overlays)) {
            $overlay.sourcePath = Assert-RelativePath ([string]$overlay.sourcePath) "$($resource.id).overlay.sourcePath"
            $overlay.target = Assert-RelativePath ([string]$overlay.target) "$($resource.id).overlay.target"
        }
    }
    $target = Get-ResourceTarget $resource
    Assert-PathWithin $target $repo "$($resource.id) 目标"
    if ($targetPaths.ContainsKey($resource.id)) { throw "资源 id 重复：$($resource.id)。" }
    foreach ($existing in $targetPaths.GetEnumerator()) {
        if (Test-PathWithin $target $existing.Value -or Test-PathWithin $existing.Value $target) {
            throw "资源目标重叠，拒绝不明确的获取顺序：$($resource.id) 与 $($existing.Key)。"
        }
    }
    $targetPaths[$resource.id] = $target
}

function Get-ExternalResources {
    return @($resources | Where-Object {
        [string]$_.source.type -ne 'repository' -and
        [string]$_.source.type -ne 'none' -and
        [string]$_.source.type -ne 'generated'
    })
}

function Validate-RepositoryTargets([switch]$IncludeExternal, [switch]$SkipGenerated, [ValidateSet('acquired', 'final')][string]$Phase = 'final') {
    foreach ($resource in $resources) {
        $kind = [string]$resource.source.type
        if ($kind -eq 'none') {
            Write-Host "[INFO] $($resource.id)：$($resource.source.description)"
            continue
        }
        if ($kind -ne 'repository' -and -not $IncludeExternal) {
            Write-Host "[SKIP] $($resource.id)：外部资源未在 Repository 范围验证。"
            continue
        }
        if ($SkipGenerated -and $kind -eq 'generated') {
            Write-Host "[SKIP] $($resource.id)：等待导出器生成补丁仓库。"
            continue
        }
        $target = Get-ResourceTarget $resource
        [void](Test-Resource $resource $target "$($resource.id) 目标" -RequireHash -Phase $Phase)
    }
}

Write-Host "Resource baseline: action=$Action scope=$Scope manifest=$manifestPath"
if ($Action -eq 'Validate') {
    if ($Scope -eq 'All') { Validate-RepositoryTargets -IncludeExternal -Phase final }
    else { Validate-RepositoryTargets -Phase final }
}
else {
    if ($Scope -ne 'All') { throw 'Acquire 必须使用 -Scope All，避免只获取一部分资源。' }
    if ([string]::IsNullOrWhiteSpace($externalRoot)) { throw 'Acquire 必须提供 -ExternalRoot 外部镜像目录。' }

    $externalResources = Get-ExternalResources
    # 先验证所有源和目标，再创建任何临时目录或写入仓库。
    foreach ($resource in $externalResources) {
        $source = Get-ResourceSource $resource
        if (-not (Test-Path -LiteralPath $source -PathType Container)) {
            Add-Failure "$($resource.id) 源：目录不存在：$source。"
            continue
        }
        [void](Test-Resource $resource $source "$($resource.id) 源" -RequireHash -Phase source)
        try { Test-ResourceOverlays $resource "$($resource.id)" } catch { Add-Failure "$($resource.id)：$($_.Exception.Message)" }
        [void](Test-TargetEmpty (Get-ResourceTarget $resource) "$($resource.id) 目标")
        if (Test-PathWithin $source (Get-ResourceTarget $resource) -or Test-PathWithin (Get-ResourceTarget $resource) $source) {
            Add-Failure "$($resource.id)：源与目标重叠，拒绝获取。"
        }
    }
    if ($failures.Count -eq 0) {
        $stageRoot = Join-Path $repo ('.resource-acquire-' + [Guid]::NewGuid().ToString('N'))
        Assert-PathWithin $stageRoot $repo '临时获取目录'
        New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
        try {
            foreach ($resource in $externalResources) {
                $source = Get-ResourceSource $resource
                $stageTarget = Join-Path $stageRoot ($resource.path.Replace('/', [IO.Path]::DirectorySeparatorChar))
                New-Item -ItemType Directory -Path (Split-Path -Parent $stageTarget) -Force | Out-Null
                New-Item -ItemType Directory -Path $stageTarget -Force | Out-Null
                Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $stageTarget -Recurse
                if ($null -ne $resource.overlays) {
                    foreach ($overlay in @($resource.overlays)) {
                        $overlaySource = Join-Path $externalRoot ($overlay.sourcePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
                        $overlayTarget = Join-Path $stageTarget ($overlay.target.Replace('/', [IO.Path]::DirectorySeparatorChar))
                        Assert-PathWithin $overlayTarget $stageTarget "$($resource.id) overlay 目标"
                        New-Item -ItemType Directory -Path (Split-Path -Parent $overlayTarget) -Force | Out-Null
                        Copy-Item -LiteralPath $overlaySource -Destination $overlayTarget
                    }
                }
                [void](Test-Resource $resource $stageTarget "$($resource.id) 临时目标" -RequireHash -Phase acquired)
            }
            if ($failures.Count -eq 0) {
                foreach ($resource in $externalResources) {
                    $target = Get-ResourceTarget $resource
                    $stageTarget = Join-Path $stageRoot ($resource.path.Replace('/', [IO.Path]::DirectorySeparatorChar))
                    if (-not (Test-TargetEmpty $target "$($resource.id) 目标")) { continue }
                    $parent = Split-Path -Parent $target
                    Assert-NoReparsePath $parent "$($resource.id) 目标父目录" -AllowMissingLeaf
                    New-Item -ItemType Directory -Path $parent -Force | Out-Null
                    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
                    Move-Item -LiteralPath $stageTarget -Destination $parent
                }
                Validate-RepositoryTargets -IncludeExternal -SkipGenerated -Phase acquired
            }
        }
        catch { Add-Failure "获取暂存/替换失败：$($_.Exception.Message)" }
        finally {
            if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "资源基线失败：$($failures.Count) 项。" -ForegroundColor Red
    exit 1
}
Write-Host '资源基线通过。' -ForegroundColor Green
