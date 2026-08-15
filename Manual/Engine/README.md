# LyoCrystal 引擎说明书源工程

本目录是引擎用户说明书的唯一内容事实源。产品行为以代码、配置和测试为最终依据；内部设计位于仓库 `Docs/`，不得直接复制成用户承诺。

## 本地构建

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.lock.txt
.\.venv\Scripts\python.exe -m mkdocs build --strict --clean --config-file mkdocs.yml
```

或者在已安装依赖的隔离环境中运行：

```powershell
.\Build-Manual.ps1
```

本地预览：

```powershell
.\Build-Manual.ps1 -Serve
```

正式离线发布包：

```powershell
.\Build-Manual.ps1 -Package
```

生成目录 `site/` 和 `dist/` 是可重建产物，不得直接修改。`-Package` 会生成完整站点 ZIP 和旁路发布清单，记录源码提交、构建时间、工具链、依赖摘要、搜索索引摘要和 ZIP 摘要。正式发布要求清单中的 `SourceDirty=false`。

## 维护入口

- 分类和页面规范：`Docs/governance/引擎说明书维护规范.md`
- 功能页面模板：`docs/contributing/page-template.md`
- 中文搜索专有词：`search-dictionary.txt`
- 导航顺序：`mkdocs.yml` 的 `nav`

`requirements.txt` 记录直接依赖，`requirements.lock.txt` 固定经过严格构建验证的完整依赖集。常规构建使用锁定文件；升级依赖必须重新执行严格构建和中文搜索抽查。

功能没有代码、测试和运行证据时，页面必须标注“规划中”。不得仅凭设计把状态改为“已实现”。
