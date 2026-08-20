param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$OutputRoot = (Join-Path (Get-Location).Path "Build\\Mobile\\BootstrapRepo"),
    [string[]]$OnlyPackages = @()
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Info([string]$Message) {
    Write-Host $Message
}

function Normalize-AssetPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    return $Path.Replace("/", "\\").TrimStart("\\")
}

$repoRoot = (Resolve-Path $RepositoryRoot).Path
$bootstrapRoot = Join-Path $repoRoot "Client_MonoGame.Shared\\BootstrapAssets"
$sharedRoot = Join-Path $repoRoot "Client_MonoGame.Shared"
$manifestPath = Join-Path $bootstrapRoot "bootstrap-packages.json"

if (-not (Test-Path $manifestPath)) {
    throw "未找到 bootstrap-packages.json：$manifestPath"
}

Write-Info "RepositoryRoot = $repoRoot"
Write-Info "OutputRoot     = $OutputRoot"

$manifest = (Get-Content -Encoding UTF8 $manifestPath | Out-String | ConvertFrom-Json)
if ($null -eq $manifest -or $null -eq $manifest.Packs) {
    throw "bootstrap-packages.json 解析失败或 Packs 为空。"
}

$packagesOut = Join-Path $OutputRoot "Packages"
$tempRoot = Join-Path $OutputRoot "_tmp"
$indexFileName = "bootstrap-package-index.json"

New-Item -ItemType Directory -Force -Path $packagesOut | Out-Null

if (Test-Path $tempRoot) {
    Remove-Item -Recurse -Force $tempRoot
}
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem

$filter = @()
if ($OnlyPackages -and $OnlyPackages.Count -gt 0) {
    $filter = $OnlyPackages | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() }
    Write-Info ("OnlyPackages   = " + ($filter -join ", "))
}

$exported = 0
$skipped = 0
$indexPackages = New-Object System.Collections.Generic.List[object]

foreach ($pack in $manifest.Packs) {
    $name = [string]$pack.Name
    if ([string]::IsNullOrWhiteSpace($name)) {
        continue
    }

    if ($filter.Count -gt 0 -and -not ($filter -contains $name)) {
        $skipped++
        continue
    }

    if ($null -eq $pack.Assets -or $pack.Assets.Count -eq 0) {
        Write-Info "[SKIP] $name：Assets 为空。"
        $skipped++
        continue
    }

    $staging = Join-Path $tempRoot $name
    $stagingPackagesRoot = Join-Path $staging "Packages\\$name"
    New-Item -ItemType Directory -Force -Path $stagingPackagesRoot | Out-Null

    $missing = 0
    foreach ($asset in $pack.Assets) {
        $relative = Normalize-AssetPath ([string]$asset)
        if ([string]::IsNullOrWhiteSpace($relative)) { continue }

        $sourcePath = $null
        $bootstrapCandidate = Join-Path $bootstrapRoot $relative
        if (Test-Path -LiteralPath $bootstrapCandidate) {
            $sourcePath = $bootstrapCandidate
        }
        else {
            $sharedCandidate = Join-Path $sharedRoot $relative
            if (Test-Path -LiteralPath $sharedCandidate) {
                $sourcePath = $sharedCandidate
            }
        }

        if ([string]::IsNullOrWhiteSpace($sourcePath)) {
            $missing++
            continue
        }

        $destPath = Join-Path $stagingPackagesRoot $relative
        $destDir = Split-Path -Parent $destPath
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        }

        Copy-Item -Force $sourcePath $destPath
    }

    if ($missing -gt 0) {
        throw "分包 $name 导出失败：存在 $missing 个 Assets 在 BootstrapAssets 中缺失（请先修复资源一致性）。"
    }

    $zipPath = Join-Path $packagesOut "$name.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $hash = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
    $shaPath = "$zipPath.sha256"
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

if (Test-Path $tempRoot) {
    Remove-Item -Recurse -Force $tempRoot
}

try {
    $index = [PSCustomObject]@{
        GeneratedAtUtc  = (Get-Date).ToUniversalTime().ToString("o")
        ResourceVersion = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
        Packages        = @($indexPackages | Sort-Object Name)
    }

    $indexJson = ($index | ConvertTo-Json -Depth 6)

    $indexOutPath = Join-Path $packagesOut $indexFileName
    [System.IO.File]::WriteAllText($indexOutPath, $indexJson, $utf8NoBom)
    Write-Info "[OK] Index -> $indexOutPath"

    $baselinePath = Join-Path $bootstrapRoot $indexFileName

    # 注意：OnlyPackages 用于“局部导出”时，输出仓库的 index 只能包含已导出的包，否则会引用缺失 zip。
    # 但 baseline index 是客户端用于预登录更新的“壳包基线”，不能因为局部导出而丢失其它包（尤其是 core-startup）。
    if ($filter.Count -gt 0 -and (Test-Path $baselinePath)) {
        $baseline = $null
        try {
            $baseline = (Get-Content -Encoding UTF8 $baselinePath | Out-String | ConvertFrom-Json)
        }
        catch {
            $baseline = $null
        }

        $merged = @{}

        foreach ($p in @($baseline.Packages)) {
            $n = [string]$p.Name
            if (-not [string]::IsNullOrWhiteSpace($n)) {
                $merged[$n.ToLowerInvariant()] = $p
            }
        }

        foreach ($p in @($index.Packages)) {
            $n = [string]$p.Name
            if (-not [string]::IsNullOrWhiteSpace($n)) {
                $merged[$n.ToLowerInvariant()] = $p
            }
        }

        $baselineIndex = [PSCustomObject]@{
            GeneratedAtUtc  = $index.GeneratedAtUtc
            ResourceVersion = $index.ResourceVersion
            Packages        = @($merged.Values | Sort-Object Name)
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
