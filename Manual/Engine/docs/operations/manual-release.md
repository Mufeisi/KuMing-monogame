# 说明书离线发布与验证

- 功能状态：已实现
- 首次支持版本：说明书开发版 2026-08-15
- 适用平台：Windows PowerShell
- 执行权限：能够读取仓库并写入 `Manual/Engine/dist` 的构建人员
- 页面复核版本：变量系统 VAR-08

正式离线发布物由 Markdown 唯一事实源自动构建，不直接修改生成的 HTML，也不单独维护 CHM 内容副本。

## 构建依赖

在隔离 Python 环境中安装锁定依赖：

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.lock.txt
```

构建脚本调用命令名 `python`。如果隔离环境未激活，应先激活该环境，或确保当前 `python` 指向已安装锁定依赖的解释器。

## 生成离线包

```powershell
.\Build-Manual.ps1 -Package
```

该命令先执行 `mkdocs build --strict --clean`，再验证变量系统、`INITVAR`、`PARSEDECIMAL`、只读占位符、错误码和发布页均进入搜索索引。任何构建警告、错误或必要搜索入口缺失都会终止发布；成功后在 `dist/` 生成：

```text
LyoCrystal-Engine-Manual-<提交>-<UTC时间>.zip
LyoCrystal-Engine-Manual-<提交>-<UTC时间>.zip.manifest.json
```

ZIP 内含完整静态站点和本地搜索索引。旁路 JSON 清单记录：

- 完整源码提交和源码是否有未提交改动；
- UTC 构建时间与构建命令；
- Python、MkDocs 版本；
- 锁定依赖、搜索索引和 ZIP 的 SHA-256；
- 站点文件数量和发布包文件名。

正式对外发布应要求 `SourceDirty=false`。如果为 `true`，该包只可用于本地审阅，不应作为可追踪正式制品。

## 验证摘要

```powershell
$manifest = Get-Content -Raw -Encoding UTF8 .\dist\*.manifest.json | ConvertFrom-Json
$actual = (Get-FileHash -Algorithm SHA256 ".\dist\$($manifest.ArchiveFile)").Hash
$actual -eq $manifest.ArchiveSha256
```

结果必须为 `True`。随后解压 ZIP，通过本地或内网 HTTP 静态服务打开，抽查导航、图片和搜索。不要只双击单个 HTML 文件验证全文搜索。

## 发布与回滚

发布时同时归档 ZIP 和对应 manifest，不覆盖旧版本。需要回滚时，选择目标源码提交重新执行严格构建，或恢复之前已经验证过摘要的成套 ZIP 与 manifest。

说明书源、静态站点、ZIP 和可选 CHM/PDF 如果同时发布，必须来自同一源码提交。生成目录 `site/` 和 `dist/` 都是可重建产物，不进入内容事实源。

## 相关页面

- [阅读与查询说明书](../getting-started/index.md)
- [变量系统功能状态](../reference/feature-status.md)
- [兼容模式与迁移](../scripting/variables/compatibility-migration.md)
