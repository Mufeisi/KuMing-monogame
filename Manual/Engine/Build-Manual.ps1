param(
    [switch]$Serve,
    [switch]$Package,
    [ValidateRange(1024, 65535)]
    [int]$Port = 8000
)

$ErrorActionPreference = 'Stop'
$manualRoot = $PSScriptRoot

Push-Location -LiteralPath $manualRoot
try {
    & python -c "import mkdocs, material, jieba"
    if ($LASTEXITCODE -ne 0) {
        throw '缺少说明书依赖。请先在隔离的 Python 环境中执行：python -m pip install -r requirements.lock.txt'
    }

    if ($Serve) {
        if ($Package) {
            throw '-Serve 与 -Package 不能同时使用。'
        }
        & python -m mkdocs serve --strict --config-file mkdocs.yml --dev-addr "127.0.0.1:$Port"
    }
    else {
        & python -m mkdocs build --strict --clean --config-file mkdocs.yml
    }

    if ($LASTEXITCODE -ne 0) {
        throw "说明书构建失败，退出码：$LASTEXITCODE"
    }

    if ($Package) {
        $repoRoot = (Resolve-Path (Join-Path $manualRoot '..\..')).Path
        $sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
            throw '无法读取说明书源码提交，拒绝生成不可追踪的发布包。'
        }
        $shortCommit = $sourceCommit.Substring(0, 12)
        $sourceDirty = @(& git -C $repoRoot status --porcelain -- Manual/Engine).Count -gt 0
        if ($LASTEXITCODE -ne 0) {
            throw '无法检查说明书源码状态。'
        }

        $sitePath = Join-Path $manualRoot 'site'
        $searchIndexPath = Join-Path $sitePath 'search\search_index.json'
        if (-not (Test-Path -LiteralPath $searchIndexPath -PathType Leaf)) {
            throw '严格构建未生成搜索索引，拒绝打包。'
        }
        $searchData = Get-Content -Raw -Encoding UTF8 -LiteralPath $searchIndexPath | ConvertFrom-Json
        $searchText = (($searchData.docs | ForEach-Object {
            "$($_.title) $($_.text) $($_.location)"
        }) -join "`n").Replace([string][char]0x200B, '')
        $requiredSearchTerms = @(
            '变量系统',
            'INITVAR',
            'PARSEDECIMAL',
            '只读系统占位符',
            'VAR08-SENSITIVE-001',
            '说明书离线发布与验证'
        )
        $missingSearchTerms = @($requiredSearchTerms | Where-Object { -not $searchText.Contains($_) })
        if ($missingSearchTerms.Count -gt 0) {
            throw "搜索索引缺少必要入口：$($missingSearchTerms -join '、')"
        }

        $distPath = Join-Path $manualRoot 'dist'
        [void](New-Item -ItemType Directory -Path $distPath -Force)
        $buildTimeUtc = [DateTime]::UtcNow
        $buildStamp = $buildTimeUtc.ToString('yyyyMMdd-HHmmss')
        $archiveName = "LyoCrystal-Engine-Manual-$shortCommit-$buildStamp.zip"
        $archivePath = Join-Path $distPath $archiveName
        Compress-Archive -Path (Join-Path $sitePath '*') -DestinationPath $archivePath -CompressionLevel Optimal

        $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
        $dependencyHash = (Get-FileHash -LiteralPath (Join-Path $manualRoot 'requirements.lock.txt') -Algorithm SHA256).Hash
        $searchIndexHash = (Get-FileHash -LiteralPath $searchIndexPath -Algorithm SHA256).Hash
        $manifest = [ordered]@{
            Product = 'LyoCrystal 引擎说明书'
            SourceCommit = $sourceCommit
            SourceDirty = $sourceDirty
            BuildTimeUtc = $buildTimeUtc.ToString('o')
            BuildCommand = '.\Build-Manual.ps1 -Package'
            PythonVersion = (& python --version 2>&1 | Out-String).Trim()
            MkDocsVersion = (& python -m mkdocs --version 2>&1 | Out-String).Trim()
            RequirementsLockSha256 = $dependencyHash
            SiteFileCount = @(Get-ChildItem -LiteralPath $sitePath -Recurse -File).Count
            SearchIndexSha256 = $searchIndexHash
            SearchTermsVerified = $requiredSearchTerms
            ArchiveFile = $archiveName
            ArchiveSha256 = $archiveHash
        }
        $manifestPath = "$archivePath.manifest.json"
        $manifestJson = $manifest | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText(
            $manifestPath,
            $manifestJson + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false))

        Write-Output "说明书离线包：$archivePath"
        Write-Output "发布清单：$manifestPath"
        Write-Output "SHA-256：$archiveHash"
    }
}
finally {
    Pop-Location
}
