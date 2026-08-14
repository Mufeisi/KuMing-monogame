# LEG-09 / INSTANCE-02 验证证据

## 任务简报

- 目标：交付可实际启动、观察、停止并在失败时回滚的服务实例运行时。
- 做：依赖拓扑、隐藏进程、端口与根目录互斥预检、TCP/HTTP 健康、版本核对、分组件日志、正常停止、显式确认强制停止和失败回滚。
- 不做：正式环境自动运行、共享账号库、作者工作台界面。
- 方法约束：复用操作系统进程、TCP/HTTP 和现有测试入口；不记录参数或秘密值；失败只清理本次创建的进程。
- 预估时间：1 个实现切片。
- 完成定义：真实隐藏组件按依赖进入健康；运行时长、版本、PID、日志和状态可观察；后置失败逆序清理；未授权正式实例失败关闭。
- 语言：中文，代码标识符、命令和原始错误除外。

## 工件与验证

- 工件：`ServiceInstanceRuntime` 状态机与运行快照、审计事件、真实隐藏进程集成测试。
- `dotnet build src/Launcher/Launcher.InstanceManagement/Launcher.InstanceManagement.csproj -c Release --no-restore`：0 警告，0 错误。
- `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~ServiceInstanceRuntimeTests|FullyQualifiedName~ServiceInstanceProfileTests" --no-restore`：6 项通过，0 项失败。
- 集成测试使用隐藏 `cmd.exe` 副本；验证依赖启动顺序、HTTP 健康、正常停止请求、秘密日志脱敏、健康超时与逆序强制清理审计。

## 安全与回滚

- 正式环境默认拒绝；档案参数中的疑似秘密值在预检阻断。
- 子进程仅获得实例、组件、区服和有效端口等非秘密环境变量。
- 用户强制停止需要显式确认；启动失败回滚只清理本次启动的进程树并记录审计。
- 回滚本切片提交不会修改数据库或运行配置；异常退出后实例根锁随进程句柄释放。

## 每日工件检查

- 可运行工件数量：运行状态机 1、真实进程集成测试 1。
- 过程资产数量：验证证据 1；未超过工件数量。
- 语言：新增文档与提交信息使用中文。
