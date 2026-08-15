param(
    [switch]$Serve,
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
        & python -m mkdocs serve --strict --config-file mkdocs.yml --dev-addr "127.0.0.1:$Port"
    }
    else {
        & python -m mkdocs build --strict --clean --config-file mkdocs.yml
    }

    if ($LASTEXITCODE -ne 0) {
        throw "说明书构建失败，退出码：$LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
