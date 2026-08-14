# WORKBENCH-03：路线终审与完整回归

- 日期：2026-08-14
- 结果：通过，`GATE-ROUTE-CLOSURE` 关闭
- 语言：中文；命令、代码标识符和原始警告除外

## 任务简报

- 目标：以可执行回归、三端构建和文档工件关闭 LEG-10 与传奇类参考源码吸收路线。
- 做：复核 LEG-00～LEG-10 证据链；复核平行路径；同步模块地图、开发者指南、运行手册和文档索引；运行完整回归与 PC、Android、服务端构建。
- 不做：不开发未激活候选；不删除缺少等价回归证明的生产路径；不生成正式签名发布物；不获取外部授权资源。
- 方法约束：统一工作台只聚合既有事实；协议、脚本、渲染、微端、发布和 Schema 继续使用唯一既有接缝。
- 预估时间：30～60 分钟；完整回归和三端构建只运行一次。
- 完成定义：所有已激活阶段有专项规格和 Evidence；候选决定齐全；完整回归与三端 Release 构建通过；文档入口与实现一致。

## 已激活阶段证据链

| 阶段 | 专项状态 | 代表证据 |
|---|---|---|
| LEG-00 | 基线完成 | [`../LEG-00-20260813/README.md`](../LEG-00-20260813/README.md) |
| LEG-01 | 已完成 | [`../LEG-01-20260813/README.md`](../LEG-01-20260813/README.md) |
| LEG-02 | 已完成 | [`../LEG-02-20260813/README.md`](../LEG-02-20260813/README.md) |
| LEG-03 | 已完成 | [`../LEG-03-20260813/README.md`](../LEG-03-20260813/README.md) |
| LEG-04 | 已完成 | [`../LEG-04-20260813/README.md`](../LEG-04-20260813/README.md) |
| LEG-05 | 已完成 | [`../LEG-05-20260813/README.md`](../LEG-05-20260813/README.md) |
| LEG-06 | 已完成 | [`../LEG-06-20260813/CONTENT-06.md`](../LEG-06-20260813/CONTENT-06.md) |
| LEG-07 | 已完成 | [`../LEG-07-20260814/GUI-13.md`](../LEG-07-20260814/GUI-13.md) |
| LEG-08 | 已完成 | [`../LEG-08-20260814/SKILL-06.md`](../LEG-08-20260814/SKILL-06.md) |
| LEG-09 | 已完成 | [`../LEG-09-20260814/INSTANCE-03.md`](../LEG-09-20260814/INSTANCE-03.md) |
| LEG-10 | 已完成 | [`WORKBENCH-01.md`](WORKBENCH-01.md)、[`WORKBENCH-02.md`](WORKBENCH-02.md)、本文 |

## 平行路径与候选终审

- 候选与合区决定已记录于 [`../../requirements/LEG-10-候选与平行路径关闭记录.md`](../../requirements/LEG-10-候选与平行路径关闭记录.md)。合区准入事实不足，关闭且未实现工具。
- 模块地图复核确认协议使用 `src/Shared/Shared`，脚本使用 `src/Server/Server/Scripting`，渲染使用 `DXManager`/`SpriteBatchStack`，微端使用 `Bootstrap*`，发布使用现有签名发布链，Schema 使用 `SchemaMigrator`。
- 未发现具备等价行为回归、可安全删除的第二套生产路径，因此本切片没有删除代码；保留既有路径比无证据删除更符合退出条件。

## 验证结果

| 验证 | 结果 |
|---|---|
| `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore` | 通过，519/519，0 失败 |
| `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj -c Release --no-restore` | 通过，126/126，0 失败 |
| `dotnet build LyoCrystal.Windows.slnf -c Release --no-restore` | 通过，0 错误；482 个既有编译警告 |
| `dotnet build LyoCrystal.Server.slnf -c Release --no-restore` | 通过，0 警告，0 错误 |
| `dotnet build src/Clients/Client_MonoGame.Android/Client_MonoGame.Android.csproj -c Release` | 通过，0 错误；2909 个既有编译警告 |
| Markdown 相对链接 | 通过，215 个受跟踪文档 |
| `git diff --check` | 通过 |

本地 Android 构建证明源码 Release 可构建，不代表正式 arm64 AOT 签名制品；正式制品仍由 CI 发布链拥有。编译警告未在本任务范围内批量整改，且未阻断既有门禁。

## 文档与运行工件

- [`../../guides/模块地图.md`](../../guides/模块地图.md)：补充 `Launcher.Workbench` 所有权与禁止复制事实的边界。
- [`../../guides/开发者指南.md`](../../guides/开发者指南.md)：校准 Launcher 过滤器工程数、审查工件位置和入口。
- [`../../runbooks/operations/LEG-10-统一作者工作台运行手册.md`](../../runbooks/operations/LEG-10-统一作者工作台运行手册.md)：统一预检、快照、差异、测试发布、故障和回滚步骤。

## 每日工件检查

- 运行工件：完整回归输出 2 份、三端构建输出 3 份。
- 代码/功能工件：WORKBENCH-01、WORKBENCH-02 已交付统一入口、版本差异与测试发布审查；本切片交付可直接执行的 Runbook 和终审证据。
- 过程资产：本证据 1 份；工件数量高于过程资产。
- 语言：文档、状态与提交信息保持中文。
