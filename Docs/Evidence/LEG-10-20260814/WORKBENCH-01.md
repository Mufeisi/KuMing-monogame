# LEG-10 / WORKBENCH-01 验证证据

## 任务简报

- 目标：交付保留原模块事实所有者的统一版本、能力与预检入口。
- 做：工作台事实模型、提供器失败隔离、统一总览、项目/发行体/入口/实例预检聚合、服务实例 Schema 与脚本目标版本。
- 不做：重写预检规则、替代发布事实源、合服器、删除平行入口。
- 方法约束：聚合器只编排；版本与预检由现有模块计算；不扫描或保存秘密；不创建分析工具。
- 预估时间：1 个实现切片。
- 完成定义：工作台显示版本、能力、Owner 和状态；统一预检保留原失败；单个提供器失败不影响其他事实。
- 语言：中文，代码标识符、命令和原始错误除外。

## 工件与验证

- 工件：`Launcher.Workbench` 深模块、作者工作台“统一工作台”页、编辑器事实适配器和自动化测试。
- `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~WorkbenchOverviewTests" --no-restore`：2 项通过，0 项失败。
- `dotnet build src/Launcher/Launcher.Editor/Launcher.Editor.csproj -c Release --no-restore`：0 警告，0 错误。
- `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj -c Release --filter "FullyQualifiedName~UnifiedWorkbenchShowsOwnedVersionsCapabilitiesAndAggregatedPreflights|FullyQualifiedName~AuthorWorkbenchRunsIsolatedServiceInstanceThroughPreflightHealthLogsAndStop" --no-restore`：2 项通过，0 项失败。
- 集成测试确认玩家入口、实例 Schema 17、脚本修订 `content-v1`、至少 6 类 Owner 和合服候选关闭事实；现有项目布局预检失败在“项目发布预检” Owner 下可见，没有被聚合层吞掉。

## 回滚与安全

- 回滚本切片会移除统一总览和纯聚合模块，不改变原预检、发布、实例运行和签名状态。
- 聚合输出不包含秘密值；服务实例只读取档案中的公开版本和 `secret://` 引用边界。
- 工作台显示的 Schema、脚本和组件值是实例档案声明的目标版本，不冒充在线运行实例实测值。

## 每日工件检查

- 可运行工件数量：工作台核心 1、统一总览页 1、核心与 Windows 集成测试 2。
- 过程资产数量：立项规格 1、验证证据 1；未超过工件数量。
- 语言：新增文档与提交信息使用中文。
