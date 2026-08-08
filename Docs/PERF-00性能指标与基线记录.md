# PERF-00 性能指标与基线记录

## 当前状态

本文件只记录 PERF-00 的采集约定与真实采集状态，不把合成数值当作压测基线。指标接缝已加入共享层，默认关闭，调用方通过 `Shared.Diagnostics.PerformanceMetrics.Configure(true, 场景名)` 显式启用，并使用 `TryFreezeAndWriteSnapshot` 导出 JSON。冻结导出会等待正在写入的指标排空；同一路径并发导出通过临时文件和路径锁保证不会留下半截 JSON。

截至 2026-08-08，仓库没有可驱动 300 个网络连接的既有机器人、测试客户端或负载脚本。`Server/MirEnvir/Robot.cs` 是 NPC 行为脚本机器人，不产生连接规模；`Tests/Base05.Tests` 也没有连接型压测入口。因此下表的“待真实采集”是当前事实，不能用单元测试输出替代。

## 固定场景定义（依据 PRD §5）

| 场景 | 连接口径 | 活跃口径 | 动作约定 | 状态 |
|---|---:|---:|---|---|
| S1 空闲连接 | 1 | 0 | 保持连接与心跳，记录 10 分钟 | 待真实采集（缺负载入口） |
| S2 活跃战斗 | 100 | 100 | 角色移动、战斗、拾取循环，记录 10 分钟 | 待真实采集（缺负载入口） |
| S3 规划容量 | **300** | **100** | 200 个空闲连接 + 100 个活跃角色，记录 10 分钟 | 待真实采集（缺负载入口） |

三组场景名称和连接/活跃口径固定后，后续压测必须复用相同动作、时长、地图、资源版本和配置，并保留指标 JSON、服务端版本提交号、资源摘要及运行日志。S3 的“300 连接/100 活跃角色”是 PRD §5 的规划测试口径，不代表当前容量已验证。

## 指标字段

`PerformanceMetricKind` 提供 CPU、Update、Draw、DrawCall、TextureSwitch、TextureCreate、Gc、GcPause、Memory、GpuMemory、Save、SaveSnapshotCapture、SaveTransactionCommit、SaveFailure、NetworkQueue、NetworkInQueue、NetworkOutQueue、Connections、ActiveConnections、Disconnects 字段。耗时通过 `RecordDuration`/`Begin` 采集；队列深度、连接口径和托管内存通过 `SetGauge`/`SampleRuntime` 采集。`Gc` 是运行时累计 collection count，`GcPause` 是运行时累计暂停时间增量（先将运行时的 TimeSpan ticks 换算为 Stopwatch ticks；运行时不支持时写 `Available=false` 与原因），两者不互相替代。PC/移动端当前没有稳定显存预算 API，`GpuMemory` 明确写 `Available=false`，不以 0 冒充。所有有样本的耗时/数值字段均导出 p95/p99；调用方应在渲染接缝、主循环、保存事务和网络队列接缝调用，而不是在业务对象中跨线程写玩家状态。

## 采集入口与口径

| 指标域 | 已接入入口 | 口径与限制 |
|---|---|---|
| CPU/Update/Draw | PC `Client_VorticeDX11/Forms/CMain.cs`；移动端 `Client_MonoGame.Shared/CMain.cs`；服务端 `Server/MirEnvir/Envir.cs` | `Begin` 包围主循环/更新/绘制接缝，记录墙钟耗时，不等同操作系统进程 CPU 百分比。|
| DrawCall/TextureSwitch | PC `Client_VorticeDX11/MirGraphics/DXManager.cs`；移动端 `Client_MonoGame.Shared/MirGraphics/SpriteBatchStack.cs` | PC 在实际绘制接缝记录调用，并按 D3D 纹理绑定指针记录切换；移动端记录 SpriteBatch 批次 Begin/状态边界，作为后端不暴露原始 GPU 计数时的代理，不能当作 GPU 硬件计数。|
| TextureCreate | PC DX11 直接 `CreateTexture2D` 入口；移动端 `MLibrary`、FairyGUI、文本框/场景占位纹理等直接 `new Texture2D`/`FromStream` 入口 | 统计仓库可见的直接创建入口；`Content.Load<Texture2D>` 内部创建由框架管理，当前不宣称已覆盖。|
| GC/Memory | PC/移动端主循环每秒 `SampleRuntime` | `Memory` 为 `GC.GetTotalMemory(false)`；`Gc` 为三代 collection count 之和；`GcPause` 为两次采样间累计暂停增量。|
| GpuMemory | PC `CMain`、移动端 `CMain` | 当前后端没有稳定显存预算 API，记录 `Available=false` 和原因，不写 0。|
| Save* | `Server/Persistence/Sql/SqlDomainTransactionRunner.cs` | `SaveSnapshotCapture` 只覆盖快照工厂；`SaveTransactionCommit` 按每次事务尝试记录；`Save` 覆盖一次保存调用（含重试总耗时）；失败单独计数。|
| Network*/Connections | 服务端 `Envir` + `MirConnection`；移动端 `CMain` + `MirNetwork/Network` | 队列深度为采样时接收/发送队列条目数；服务端连接数为 `Connections.Count`，活跃数为 `Players.Count`；断线在 `MirConnection.Disconnect` 计数。|

性能会话只在显式 `Configure(true, 场景名)`/`StartSession(场景名)` 后采样；发布默认关闭。典型导出调用如下（压测驱动负责在真实场景结束时调用，路径应保存为场景工件）：

```csharp
PerformanceMetrics.Configure(enabled: true, scenario: "S3");
// 运行固定场景 10 分钟……
PerformanceMetrics.TryFreezeAndWriteSnapshot(
    "artifacts/perf-00/S3-<提交号>.json",
    out _,
    out var error);
```

若导出失败，必须保留 `error` 和运行日志；不能只保留人工抄录的 p95/p99 数字。

## 自动验证证据（不等同三场景基线）

2026-08-08 在 SDK `10.0.200` 下执行：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~PerformanceMetricsTests|FullyQualifiedName~SqlPersistenceRoundTripTests"
结果：6/6 通过（含快照导出并发、会话隔离、GC/内存采样与 SQL 保存三类耗时断言）。
```

测试会在系统临时目录生成并校验 JSON 后清理文件；这些样本用于验证采集接缝和格式，不包含真实网络连接，不能作为 S1/S2/S3 基线。完整测试和平台构建结果由任务提交记录保留。

同日完整测试命令：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo
结果：142/142 通过，0 失败，0 跳过。
```

## 真实性与关闭条件

PERF-00 在连接型压测入口补齐前尚未关闭。补齐后，需按 S1/S2/S3 各运行一次真实采集，保存三份 JSON 工件，并在报告中标注运行时长、实际连接数、活跃数、版本与失败/断线计数；不得以“模拟 300 连接”或合成样本声称门禁通过。
