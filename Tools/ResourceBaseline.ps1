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

function Get-TextSha256([string]$Value) {
    if ($null -eq $Value) { throw '无法为 null 文本计算 SHA256。' }
    $normalized = $Value.Trim().Normalize([Text.NormalizationForm]::FormC)
    $digest = [Security.Cryptography.SHA256]::Create().ComputeHash($utf8NoBom.GetBytes($normalized))
    return ([BitConverter]::ToString($digest) -replace '-', '').ToLowerInvariant()
}

function Test-ManifestProperty($Object, [string]$Name) {
    return $null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]
}

function Assert-ManifestString($Object, [string]$Name, [string]$Label, [switch]$AllowNull) {
    if (-not (Test-ManifestProperty $Object $Name)) {
        if ($AllowNull) { return $null }
        throw "$Label 缺少必需字段 $Name。"
    }
    $value = [string]$Object.$Name
    if (-not $AllowNull -and [string]::IsNullOrWhiteSpace($value)) {
        throw "$Label.$Name 不能为空。"
    }
    return $value
}

function Assert-ManifestSha256($Object, [string]$Name, [string]$Label, [switch]$AllowNull) {
    $value = Assert-ManifestString $Object $Name $Label -AllowNull:$AllowNull
    if ([string]::IsNullOrWhiteSpace($value)) { return $value }
    if ($value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Label.$Name 必须是 64 位 SHA256 十六进制值：$value。"
    }
    return $value.ToLowerInvariant()
}

function Assert-ManifestBoolean($Object, [string]$Name, [string]$Label) {
    if (-not (Test-ManifestProperty $Object $Name) -or $Object.$Name -isnot [bool]) {
        throw "$Label.$Name 必须是布尔值。"
    }
    return [bool]$Object.$Name
}

function Assert-ManifestDigest($Spec, [string]$Label, [switch]$AllowNull) {
    if ($null -eq $Spec) {
        if ($AllowNull) { return }
        throw "$Label 缺少摘要对象。"
    }
    if (-not (Test-ManifestProperty $Spec 'fileCount') -or [int64]$Spec.fileCount -lt 0) {
        throw "$Label.fileCount 必须是非负整数。"
    }
    if (-not (Test-ManifestProperty $Spec 'bytes') -or [int64]$Spec.bytes -lt 0) {
        throw "$Label.bytes 必须是非负整数。"
    }
    [void](Assert-ManifestSha256 $Spec 'sha256' $Label)
}

function Assert-ManifestContract($Manifest) {
    if (-not (Test-ManifestProperty $Manifest 'contract')) {
        throw '资源清单缺少 contract 获取/验证契约。'
    }
    $contract = $Manifest.contract
    foreach ($name in @('acquire', 'validate')) {
        $item = $contract.$name
        if ($null -eq $item) { throw "资源清单 contract.$name 缺失。" }
        [void](Assert-ManifestString $item 'script' "contract.$name")
        [void](Assert-ManifestString $item 'action' "contract.$name")
        [void](Assert-ManifestString $item 'scope' "contract.$name")
        if ([string]$item.script -ne 'Tools/ResourceBaseline.ps1') {
            throw "contract.$name.script 必须固定为 Tools/ResourceBaseline.ps1。"
        }
    }
    if ([string]$contract.acquire.action -ne 'Acquire' -or [string]$contract.acquire.scope -ne 'All') {
        throw 'contract.acquire 必须固定为 ResourceBaseline.ps1 的 Acquire/All。'
    }
    if ([string]$contract.validate.action -ne 'Validate') {
        throw 'contract.validate 必须固定为 ResourceBaseline.ps1 的 Validate。'
    }
    if ([string]$contract.validate.scope -ne 'Repository|All') {
        throw 'contract.validate.scope 必须固定为 Repository|All。'
    }
    [void](Assert-ManifestString $Manifest 'repositoryResourceSourceRevision' '资源清单')
    if ([string]$Manifest.repositoryResourceSourceRevision -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'repositoryResourceSourceRevision 必须是固定的 40 位 Git 提交 SHA。'
    }
}

function Assert-ResourceContract($Resource) {
    $label = "资源 $($Resource.id)"
    if ([string]::IsNullOrWhiteSpace([string]$Resource.id)) { throw '资源 id 不能为空。' }
    [void](Assert-ManifestBoolean $Resource 'required' $label)
    if (-not $Resource.source) { throw "$label 缺少 source 来源标识。" }
    $source = $Resource.source
    [void](Assert-ManifestString $source 'type' "$label.source")
    [void](Assert-ManifestString $source 'id' "$label.source")
    [void](Assert-ManifestString $source 'locator' "$label.source")
    $kind = [string]$source.type
    $allowedKinds = @('repository', 'local-authorized', 'generated', 'none')
    if ($allowedKinds -notcontains $kind) {
        throw "$label.source.type 不在允许白名单内：$kind。"
    }
    if ([bool]$Resource.required -and $kind -eq 'none') {
        throw "$label.required=true 时 source.type 不得为 $kind。"
    }
    if ($kind -eq 'none') {
        if ([string]$source.locator -ne 'not-present') { throw "$label.source.locator 必须固定为 not-present。" }
        if (-not $source.acquisition) { throw "$label.source 缺少 absence acquisition 契约。" }
        [void](Assert-ManifestString $Resource 'version' $label)
        [void](Assert-ManifestString $source 'version' "$label.source")
        if ([string]$Resource.version -ne 'absent') {
            throw "$label.version 必须固定为 absent。"
        }
        if ([string]$source.version -ne 'absent') {
            throw "$label.source.version 必须固定为 absent。"
        }
        [void](Assert-ManifestSha256 $source 'versionSha256' "$label.source")
        if (([string]$source.versionSha256).ToLowerInvariant() -ne (Get-TextSha256 ([string]$source.version))) {
            throw "$label.source.versionSha256 必须是规范化 version 文本的 SHA256。"
        }
        if ([string]$Resource.version -ne [string]$source.version) {
            throw "$label.version 必须严格等于 source.version。"
        }
        if (-not $source.validation) { throw "$label.source 缺少 absence validation 验证契约。" }
        [void](Assert-ManifestString $source.validation 'algorithm' "$label.source.validation")
        [void](Assert-ManifestString $source.validation 'phase' "$label.source.validation")
        [void](Assert-ManifestString $source.validation 'method' "$label.source.validation")
        [void](Assert-ManifestString $source.validation 'scope' "$label.source.validation")
        if ([string]$source.validation.algorithm -ne 'SHA256' -or
            [string]$source.validation.phase -ne 'final' -or
            [string]$source.validation.method -ne 'assert-absent' -or
            [string]$source.validation.scope -ne 'target-absent') {
            throw "$label.source.validation 必须固定为 SHA256/final/assert-absent/target-absent。"
        }
        if ([string]$source.acquisition.method -ne 'not-present') { throw "$label.source.acquisition.method 必须固定为 not-present。" }
        return
    }
    [void](Assert-ManifestString $Resource 'version' $label)
    [void](Assert-ManifestString $source 'version' "$label.source")
    if ([string]$Resource.version -ne [string]$source.version) {
        throw "$label.version 必须严格等于 source.version。"
    }
    [void](Assert-ManifestSha256 $source 'versionSha256' "$label.source")
    if (([string]$source.versionSha256).ToLowerInvariant() -ne (Get-TextSha256 ([string]$source.version))) {
        throw "$label.source.versionSha256 必须是规范化 version 文本的 SHA256。"
    }
    if (-not $source.validation) { throw "$label.source 缺少 validation 验证契约。" }
    [void](Assert-ManifestString $source.validation 'algorithm' "$label.source.validation")
    [void](Assert-ManifestString $source.validation 'phase' "$label.source.validation")
    if ([string]$source.validation.algorithm -ne 'SHA256') {
        throw "$label.source.validation.algorithm 必须固定为 SHA256。"
    }
    if (-not $source.acquisition) { throw "$label.source 缺少 acquisition 获取契约。" }
    [void](Assert-ManifestString $source.acquisition 'method' "$label.source.acquisition")
    if ($kind -eq 'repository') {
        $resourcePath = ([string]$Resource.path).Replace('\', '/')
        if ([string]$source.locator -ne "git-tree:$resourcePath") {
            throw "$label.source.locator 必须固定为 git-tree:$resourcePath。"
        }
        if ([string]$source.acquisition.method -ne 'repository-tracked') {
            throw "$label.source.acquisition.method 必须固定为 repository-tracked。"
        }
    }
    elseif ($kind -eq 'local-authorized') {
        [void](Assert-ManifestString $source.acquisition 'script' "$label.source.acquisition")
        $sourceRelativePath = Assert-ManifestString $source.acquisition 'externalRootRelativePath' "$label.source.acquisition"
        if (-not [string]::IsNullOrWhiteSpace([string]$Resource.sourcePath) -and
            ([string]$Resource.sourcePath).Replace('\', '/') -ne $sourceRelativePath.Replace('\', '/')) {
            throw "$label.sourcePath 与 source.acquisition.externalRootRelativePath 不一致。"
        }
        if ([string]$source.locator.Replace('\', '/') -ne $sourceRelativePath.Replace('\', '/')) {
            throw "$label.source.locator 必须固定为 externalRootRelativePath。"
        }
        if ([string]$source.acquisition.script -ne 'Tools/ResourceBaseline.ps1' -or
            [string]$source.acquisition.action -ne 'Acquire' -or
            [string]$source.acquisition.scope -ne 'All') {
            throw "$label.source.acquisition 必须声明 ResourceBaseline.ps1 Acquire/All。"
        }
    }
    elseif ($kind -eq 'generated') {
        [void](Assert-ManifestString $source.acquisition 'script' "$label.source.acquisition")
        $generatorScript = Assert-RelativePath ([string]$source.acquisition.script) "$label.source.acquisition.script"
        if (-not (Test-Path -LiteralPath (Join-Path $repo ($generatorScript.Replace('/', [IO.Path]::DirectorySeparatorChar))) -PathType Leaf)) {
            throw "$label.source.acquisition.script 不存在：$generatorScript。"
        }
        if ([string]$source.acquisition.action -ne 'Export') {
            throw "$label.source.acquisition.action 必须为 Export。"
        }
        if ([string]$source.locator.Replace('\', '/') -ne ([string]$source.acquisition.script).Replace('\', '/')) {
            throw "$label.source.locator 必须固定为生成脚本路径。"
        }
    }
    if ($kind -ne 'none') {
        $sourceSpec = $source
        [void](Assert-ManifestDigest $sourceSpec "$label.source")
        $phase = if ($kind -eq 'local-authorized') { 'source' } else { 'final' }
        if ([string]$source.validation.phase -ne $phase) {
            throw "$label.source.validation.phase 必须绑定 $phase 阶段。"
        }
        $phaseSpec = $Resource.PSObject.Properties[$phase].Value
        if ($null -eq $phaseSpec) {
            if ($phase -eq 'final' -and (Test-ManifestProperty $Resource 'fileCount')) {
                $phaseSpec = $Resource
            }
            else { throw "$label 缺少 $phase 摘要。" }
        }
        [void](Assert-ManifestDigest $phaseSpec "$label.$phase")
        foreach ($field in @('fileCount', 'bytes', 'sha256')) {
            if ([string]$sourceSpec.$field -ne [string]$phaseSpec.$field) {
                throw "$label.source 摘要必须严格等于 $phase 阶段摘要（字段 $field）。"
            }
        }
        if ($kind -eq 'local-authorized') {
            if ($null -eq $Resource.acquired -or $null -eq $Resource.final) {
                throw "$label 必须同时提供 acquired 与 final 摘要。"
            }
            [void](Assert-ManifestDigest $Resource.acquired "$label.acquired")
            [void](Assert-ManifestDigest $Resource.final "$label.final")
        }
        elseif ($null -eq $Resource.final) {
            throw "$label 必须提供 final 摘要。"
        }
    }
    if ($Resource.repositoryOverlay) {
        if ($kind -ne 'local-authorized') { throw "$label.repositoryOverlay 仅允许 local-authorized 来源。" }
        [void](Assert-ManifestDigest $Resource.repositoryOverlay "$label.repositoryOverlay")
    }
}

function Test-ArtifactRecords($Resource, [string]$Root, [string]$Label, $Spec) {
    if ($null -eq $Spec -or $null -eq $Spec.artifacts) { return }
    $indexPath = $null
    foreach ($artifact in @($Spec.artifacts)) {
        $relative = Assert-RelativePath ([string]$artifact.path) "$($Resource.id).artifacts.path"
        if ($relative -like '*bootstrap-package-index.json') { $indexPath = $relative }
        $candidate = Join-Path $Root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            Add-Failure "$Label：缺少制品 $relative。"
            continue
        }
        $item = Get-Item -LiteralPath $candidate -Force
        if ($null -ne $artifact.bytes -and [Int64]$artifact.bytes -ne [Int64]$item.Length) {
            Add-Failure "$Label 制品 $relative：文件大小不匹配，期望 $($artifact.bytes)，实际 $($item.Length)。"
        }
        try { $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate).Hash.ToLowerInvariant() }
        catch { Add-Failure "$Label 制品 $relative：无法计算 SHA256：$($_.Exception.Message)"; continue }
        if ([string]::IsNullOrWhiteSpace([string]$artifact.sha256)) {
            Add-Failure "$Label 制品 $relative：SHA256 未固定。"
        }
        elseif ($hash -ne ([string]$artifact.sha256).ToLowerInvariant()) {
            Add-Failure "$Label 制品 $relative：SHA256 不匹配，期望 $($artifact.sha256)，实际 $hash。"
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$artifact.sidecar)) {
            $sidecarRelative = Assert-RelativePath ([string]$artifact.sidecar) "$($Resource.id).artifacts.sidecar"
            $sidecar = Join-Path $Root ($sidecarRelative.Replace('/', [IO.Path]::DirectorySeparatorChar))
            if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
                Add-Failure "$Label 制品 $relative：缺少 SHA256 sidecar $sidecarRelative。"
            }
            else {
                $declared = (Get-Content -Raw -LiteralPath $sidecar).Trim().ToLowerInvariant()
                if ($declared -ne $hash) {
                    Add-Failure "$Label 制品 $relative：sidecar SHA256 不匹配，声明 $declared，实际 $hash。"
                }
            }
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($indexPath)) {
        Test-PackageIndexRecords $Root $indexPath $Label
    }
}

function Test-PackageIndexRecords([string]$Root, [string]$IndexRelativePath, [string]$Label) {
    $indexPath = Join-Path $Root ($IndexRelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) { return }
    try {
        $index = Get-Content -Raw -Encoding UTF8 -LiteralPath $indexPath | ConvertFrom-Json
        if ($null -eq $index -or $null -eq $index.Packages) {
            Add-Failure "$Label：补丁索引 Packages 为空。"
            return
        }
        $records = @($index.Packages)
        if ($records.Count -eq 0) {
            Add-Failure "$Label：补丁索引 Packages 不能为空。"
            return
        }
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($record in $records) {
            $name = [string]$record.Name
            if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
                Add-Failure "$Label：索引分包名称非法：$name。"
                continue
            }
            if (-not $names.Add($name)) {
                Add-Failure "$Label：索引分包名称重复：$name。"
                continue
            }
            $zipPath = Join-Path (Split-Path -Parent $indexPath) ($name + '.zip')
            $sidecarPath = $zipPath + '.sha256'
            if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
                Add-Failure "$Label：索引分包 $name 缺少 ZIP。"
                continue
            }
            $zip = Get-Item -LiteralPath $zipPath -Force
            $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
            if ($null -eq $record.Size -or [Int64]$record.Size -ne [Int64]$zip.Length) {
                Add-Failure "$Label：索引分包 $name 大小不匹配，期望 $($record.Size)，实际 $($zip.Length)。"
            }
            if ([string]$record.Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
                $actualHash -ne ([string]$record.Sha256).ToLowerInvariant()) {
                Add-Failure "$Label：索引分包 $name SHA256 不匹配，期望 $($record.Sha256)，实际 $actualHash。"
            }
            if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
                Add-Failure "$Label：索引分包 $name 缺少 SHA256 sidecar。"
            }
            else {
                $declared = (Get-Content -Raw -LiteralPath $sidecarPath).Trim().Split([char[]]" `t`r`n")[0].ToLowerInvariant()
                if ($declared -ne $actualHash) {
                    Add-Failure "$Label：索引分包 $name sidecar SHA256 不匹配，声明 $declared，实际 $actualHash。"
                }
            }
        }
        $packageRoot = Split-Path -Parent $indexPath
        $zipNames = @(Get-ChildItem -LiteralPath $packageRoot -File -Filter '*.zip' | ForEach-Object { $_.BaseName })
        foreach ($zipName in $zipNames) {
            if (-not $names.Contains($zipName)) { Add-Failure "$Label：存在未被索引的 ZIP：$zipName。" }
        }
        foreach ($sidecar in @(Get-ChildItem -LiteralPath $packageRoot -File -Filter '*.zip.sha256')) {
            $sideName = $sidecar.Name.Substring(0, $sidecar.Name.Length - '.zip.sha256'.Length)
            if (-not $names.Contains($sideName)) { Add-Failure "$Label：存在未被索引的 sidecar：$($sidecar.Name)。" }
        }
    }
    catch {
        Add-Failure "$Label：补丁索引解析失败：$($_.Exception.Message)"
    }
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
    # locator 是唯一的源路径依据；acquisition.externalRootRelativePath 仅用于契约一致性校验。
    $relative = [string]$Resource.source.locator
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative)) {
        throw "$($Resource.id) source.locator 必须是 externalRoot 下的相对路径。"
    }
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
    elseif ($Phase -eq 'acquired' -and $Resource.final) {
        # repository/generated 资源没有独立 acquired 阶段，获取后仍按 final 摘要验证。
        $candidate = $Resource.final
    }
    elseif ($Phase -eq 'final' -and $Resource.acquired) {
        # 没有真实 finalize 的资源必须显式将 final 与 acquired 对齐；此回退仅便于旧兼容清单诊断。
        $candidate = $Resource.acquired
    }
    if ($null -ne $candidate) { return $candidate }
    return [PSCustomObject]@{
        fileCount = $Resource.fileCount
        bytes = $Resource.bytes
        sha256 = $Resource.sha256
        expected = $Resource.expected
    }
}

function Test-DigestSpec($Root, [string]$CanonicalPrefix, $Spec, [string]$Label) {
    if ($null -eq $Spec) {
        Add-Failure "$Label：缺少摘要对象。"
        return $false
    }
    try {
        $digest = Get-TreeDigest $Root $CanonicalPrefix
        $ok = $true
        if ($digest.FileCount -ne [int64]$Spec.fileCount -or $digest.Bytes -ne [int64]$Spec.bytes) {
            Add-Failure "$Label：文件计数/大小不匹配，期望 $($Spec.fileCount) 个/$($Spec.bytes) bytes，实际 $($digest.FileCount) 个/$($digest.Bytes) bytes。"
            $ok = $false
        }
        if ([string]::IsNullOrWhiteSpace([string]$Spec.sha256) -or
            $digest.Sha256 -ne ([string]$Spec.sha256).ToLowerInvariant()) {
            Add-Failure "$Label：目录 SHA256 不匹配，期望 $($Spec.sha256)，实际 $($digest.Sha256)。"
            $ok = $false
        }
        if ($ok) { Write-Host "[OK] $Label：$($digest.FileCount) files, $($digest.Bytes) bytes, sha256=$($digest.Sha256)" -ForegroundColor Green }
        return $ok
    }
    catch {
        Add-Failure "$Label：$($_.Exception.Message)"
        return $false
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
        if (-not [bool]$Resource.required) {
            Write-Host "[SKIP] $Label：可选资源目录不存在。"
            return $true
        }
        Add-Failure "$Label：目录不存在：$Root。"
        return $false
    }
    try {
        $failureCountBefore = $failures.Count
        Assert-NoReparseDescendants $Root $Label
        $spec = Get-ResourcePhaseSpec $Resource $Phase
        if ($null -eq $spec) {
            Add-Failure "$Label：清单缺少 $Phase 摘要。"
            return $false
        }
        Test-ExpectedFiles $Resource $Root $Label $spec.expected
        Test-ArtifactRecords $Resource $Root $Label $spec
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

function Test-ResourceAbsence($Resource, [string]$Target, [string]$Label) {
    if (Test-Path -LiteralPath $Target) {
        Add-Failure "$Label：absence 契约要求目标不存在，但发现 $Target。"
        return $false
    }
    Write-Host "[OK] $Label：按 absence 契约目标不存在。" -ForegroundColor Green
    return $true
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

function Test-TargetReadyForAcquire($Resource, [string]$Target, [string]$Label) {
    if ($Resource.repositoryOverlay) {
        if (-not (Test-Path -LiteralPath $Target)) {
            Add-Failure "$Label：仓库 overlay 目标必须已存在，缺少可叠加目录：$Target。"
            return $false
        }
        if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
            Add-Failure "$Label：仓库 overlay 目标已存在但不是目录：$Target。"
            return $false
        }
        return Test-DigestSpec $Target ([string]$Resource.path) $Resource.repositoryOverlay "$Label 仓库 overlay"
    }
    if (-not (Test-Path -LiteralPath $Target)) { return $true }
    return Test-TargetEmpty $Target $Label
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

Assert-ManifestContract $manifest

$resources = @($manifest.resources)
if ($resources.Count -eq 0) { throw '资源清单 resources 不能为空。' }
$targetPaths = @{}
$resourceIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($resource in $resources) {
    $resource.path = Assert-RelativePath ([string]$resource.path) "$($resource.id).path"
    if (-not [string]::IsNullOrWhiteSpace([string]$resource.sourcePath)) {
        $resource.sourcePath = Assert-RelativePath ([string]$resource.sourcePath) "$($resource.id).sourcePath"
    }
    if ($resource.source -and $resource.source.acquisition -and
        -not [string]::IsNullOrWhiteSpace([string]$resource.source.acquisition.externalRootRelativePath)) {
        $resource.source.acquisition.externalRootRelativePath = Assert-RelativePath `
            ([string]$resource.source.acquisition.externalRootRelativePath) `
            "$($resource.id).source.acquisition.externalRootRelativePath"
    }
    Assert-ResourceContract $resource
    if (-not $resourceIds.Add([string]$resource.id)) { throw "资源 id 重复：$($resource.id)。" }
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
            [void](Test-ResourceAbsence $resource (Get-ResourceTarget $resource) "$($resource.id) absence 目标")
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
        [void](Test-TargetReadyForAcquire $resource (Get-ResourceTarget $resource) "$($resource.id) 目标")
        if (Test-PathWithin $source (Get-ResourceTarget $resource) -or Test-PathWithin (Get-ResourceTarget $resource) $source) {
            Add-Failure "$($resource.id)：源与目标重叠，拒绝获取。"
        }
    }
    if ($failures.Count -eq 0) {
        $stageRoot = Join-Path $repo ('.resource-acquire-' + [Guid]::NewGuid().ToString('N'))
        Assert-PathWithin $stageRoot $repo '临时获取目录'
        New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
        $replacementRecords = [System.Collections.Generic.List[object]]::new()
        $backupRoot = Join-Path $stageRoot '.backups'
        $replacementCount = 0
        $failAfterReplace = $null
        $failCleanupAfterCommit = $false
        $failRollback = $false
        $committed = $false
        $preserveStage = $false
        try {
            if (-not [string]::IsNullOrWhiteSpace($env:RESOURCE_BASELINE_TEST_FAIL_AFTER_REPLACE)) {
                $parsedFailAfterReplace = 0
                if (-not [int]::TryParse($env:RESOURCE_BASELINE_TEST_FAIL_AFTER_REPLACE, [ref]$parsedFailAfterReplace) -or $parsedFailAfterReplace -lt 1) {
                    throw 'RESOURCE_BASELINE_TEST_FAIL_AFTER_REPLACE 必须是正整数。'
                }
                $failAfterReplace = $parsedFailAfterReplace
            }
            if ($env:RESOURCE_BASELINE_TEST_FAIL_BACKUP_CLEANUP -match '^(1|true)$') {
                $failCleanupAfterCommit = $true
            }
            if ($env:RESOURCE_BASELINE_TEST_FAIL_ROLLBACK -match '^(1|true)$') {
                $failRollback = $true
            }
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
                New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
                foreach ($resource in $externalResources) {
                    $target = Get-ResourceTarget $resource
                    $stageTarget = Join-Path $stageRoot ($resource.path.Replace('/', [IO.Path]::DirectorySeparatorChar))
                    if (-not (Test-TargetReadyForAcquire $resource $target "$($resource.id) 目标")) { continue }
                    $parent = Split-Path -Parent $target
                    Assert-NoReparsePath $parent "$($resource.id) 目标父目录" -AllowMissingLeaf
                    New-Item -ItemType Directory -Path $parent -Force | Out-Null
                    $record = [pscustomobject]@{
                        ResourceId = [string]$resource.id
                        Target = $target
                        Backup = $null
                        BackupMoved = $false
                        Installed = $false
                    }
                    $replacementRecords.Add($record) | Out-Null
                    if (Test-Path -LiteralPath $target) {
                        $backupTarget = Join-Path $backupRoot ('target-' + $replacementRecords.Count.ToString('D4'))
                        Assert-PathWithin $backupTarget $stageRoot "$($resource.id) 备份"
                        $record.Backup = $backupTarget
                        Move-Item -LiteralPath $target -Destination $backupTarget
                        $record.BackupMoved = $true
                    }
                    Move-Item -LiteralPath $stageTarget -Destination $parent
                    $record.Installed = $true
                    $replacementCount++
                    if ($null -ne $failAfterReplace -and $replacementCount -ge $failAfterReplace) {
                        throw "测试注入：已替换 $replacementCount 个资源后故障。"
                    }
                }
                Validate-RepositoryTargets -IncludeExternal -SkipGenerated -Phase acquired
                if ($failures.Count -gt 0) {
                    throw '替换后资源验证失败，事务回滚。'
                }
                $committed = $true
                Write-Host '[INFO] Acquire 已到达提交点：所有新目标替换与验证成功。'
                if ($failCleanupAfterCommit) {
                    throw '测试注入：提交后备份清理故障。'
                }
                foreach ($record in $replacementRecords) {
                    if ($record.BackupMoved -and $record.Backup -and (Test-Path -LiteralPath $record.Backup)) {
                        Remove-Item -LiteralPath $record.Backup -Recurse -Force
                    }
                }
            }
        }
        catch {
            $failureMessage = $_.Exception.Message
            if ($committed) {
                $preserveStage = $true
                Add-Failure "提交后备份清理失败：$failureMessage；新目标保持不变，备份目录保留：$backupRoot。"
            }
            else {
                $rollbackFailed = $false
                $rollbackInjected = $false
                for ($i = $replacementRecords.Count - 1; $i -ge 0; $i--) {
                    $record = $replacementRecords[$i]
                    try {
                        if ($failRollback -and -not $rollbackInjected) {
                            $rollbackInjected = $true
                            throw '测试注入：事务回滚故障。'
                        }
                        if ($record.Installed -and (Test-Path -LiteralPath $record.Target)) {
                            Remove-Item -LiteralPath $record.Target -Recurse -Force
                        }
                        if ($record.BackupMoved -and $record.Backup -and (Test-Path -LiteralPath $record.Backup)) {
                            $targetParent = Split-Path -Parent $record.Target
                            New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
                            Move-Item -LiteralPath $record.Backup -Destination $record.Target
                        }
                    }
                    catch {
                        $rollbackFailed = $true
                        Add-Failure "事务回滚 $($record.ResourceId) 失败：$($_.Exception.Message)"
                    }
                }
                if ($rollbackFailed) {
                    $preserveStage = $true
                    Add-Failure "事务回滚未完成，保留暂存目录与备份目录供人工恢复：$stageRoot（备份：$backupRoot）。"
                }
                else {
                    Write-Host '[INFO] 事务回滚完成，已按逆序恢复已替换目标。'
                }
                Add-Failure "获取暂存/替换失败：$failureMessage"
            }
        }
        finally {
            if ($preserveStage) {
                Write-Host "[WARN] 保留暂存目录：$stageRoot；备份目录：$backupRoot。"
            }
            elseif (Test-Path -LiteralPath $stageRoot) {
                Remove-Item -LiteralPath $stageRoot -Recurse -Force
            }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "资源基线失败：$($failures.Count) 项。" -ForegroundColor Red
    exit 1
}
Write-Host '资源基线通过。' -ForegroundColor Green
