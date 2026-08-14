# LEG-09 / INSTANCE-03 验证证据

## 任务简报

- 目标：把实例运行能力接入桌面作者工作台，并用隔离测试实例完成预检、启动、健康、日志和停止外环。
- 做：实例选择、重新载入、预检、启动、健康刷新、组件快照、日志读取、正常停止和两次确认强制停止入口。
- 不做：正式环境自动启动、共享账号库、可见桌面自动化。
- 方法约束：工作台只调用 `Launcher.InstanceManagement`，不复制档案、状态或审计事实；验证进程隐藏运行。
- 预估时间：1 个实现切片。
- 完成定义：作者工作台管理一个隔离测试实例，真实 TCP 探针确认健康，日志与审计可观察，正常停止后无残留进程。
- 语言：中文，代码标识符、命令和原始错误除外。

## 工件与验证

- 工件：作者工作台“实例”页、运维说明、模块地图和开发者入口更新。
- `dotnet build src/Launcher/Launcher.Editor/Launcher.Editor.csproj -c Release --no-restore`：0 警告，0 错误。
- `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj -c Release --filter "FullyQualifiedName~AuthorWorkbenchRunsIsolatedServiceInstanceThroughPreflightHealthLogsAndStop|FullyQualifiedName~EditorShellUsesSixStableModesAndSingleObjectTreeWorkspace" --no-restore --no-build`：2 项通过，0 项失败。
- 外环在临时项目写入实例档案，以隐藏 `cmd.exe` 启动 PowerShell TCP 组件；工作台完成预检、真实 TCP 健康、日志和正常停止，最终状态为 `Stopped`。
- 验证未使用鼠标、键盘模拟或可见桌面自动化；窗体外环不显示到用户桌面。

## 回滚与遗留边界

- 回滚本切片提交会移除工作台入口，不影响实例档案模块和运行时；实例档案、日志与运行数据不由回滚删除。
- Windows 集成工程仍存在既有依赖告警；本次 `Launcher.Editor` 受影响项目构建为 0 警告。
- 共享账号库、生产运行授权和跨机实例调度保持关闭，需独立 ADR/PRD。

## 每日工件检查

- 可运行工件数量：实例操作页 1、作者工作台真实进程外环 1。
- 过程资产数量：运维说明更新 1、验证证据 1；未超过工件数量。
- 语言：新增文档与提交信息使用中文。
