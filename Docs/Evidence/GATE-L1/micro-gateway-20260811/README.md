# GATE-L1 独立微端网关验证证据

## 工件

- 发布物：`artifacts/gate-l1/micro-gateway-win-x64/MicroGateway.App.exe`
- 平台：Windows x64，自包含单文件，无需安装 .NET SDK 或运行时
- 大小：116,132,452 字节
- SHA-256：`F670634DA7FA9B504084891C4E51995E90A72B9BED755A3438D38D9697B692AE`
- GUI 截图：`micro-gateway-gui.png`

## 已执行验证

1. `MicroGateway.Core` 不引用 `Server.Library` 或 `Envir`，独立宿主只引用核心项目。
2. 内置 `Server.HttpServer` 与独立 `MicroHttpListenerHost` 同时启动，文件 Range、图库头、图库单图、声音四类响应状态、协议头、内容类型与字节内容一致；内置模式运行期改 User/Code 继续生效。
3. `/launcher/` 留空时禁用；配置独立发布目录后无需玩家鉴权即可只读访问。目录穿越与资源根内 junction 逃逸均被拒绝；`/api/` 缺少 User/Code 返回未授权。
4. 现有 `MicroProtocolRegressionTests` 与新增契约测试合计 5/5 通过，含流式请求快照与停止等待，见 `gate-l1-contract.trx`。
5. 发布后的真实 EXE 执行 `--gateway-smoke Client_MonoGame.Shared/BootstrapAssets`，健康检查与 32 字节 Range 请求成功，退出码 0。
6. GUI 在 680×360 最小窗口下完成可见验收，无控件重叠或截断。

## 复现命令

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~MicroGatewayCoreTests|FullyQualifiedName~MicroProtocolRegressionTests"
dotnet publish MicroGateway.App/MicroGateway.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o artifacts/gate-l1/micro-gateway-win-x64
& artifacts/gate-l1/micro-gateway-win-x64/MicroGateway.App.exe --gateway-smoke Client_MonoGame.Shared/BootstrapAssets
```

## 边界说明

- 本阶段仍使用直接文件系统，不包含 L5 的扫描、稳定文件检测、索引、缓存、Windows Service、URLACL 或防火墙自动配置。
- 当前环境完成了内置与独立两个监听实例的角色隔离验证；正式交付前仍需在两台 Windows 主机上复跑同一协议链路。
- 工作树中任务开始前已有的 `AGENTS.md`、`CONTEXT.md`、执行纪律文档、构建目录及 ADR/设计文档未纳入本阶段提交。
