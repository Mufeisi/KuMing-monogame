# PERF-00 性能指标与基线记录

## 当前状态

本文件只记录 PERF-00 的采集约定与真实采集状态，不把合成数值当作压测基线。指标接缝已加入共享层，默认关闭。生产进程可设置 `LYOCRYSTAL_PERF00_ENABLED=true`、`LYOCRYSTAL_PERF00_SCENARIO=S1|S2|S3`、`LYOCRYSTAL_PERF00_OUTPUT=<json路径>`，PC、移动端和服务端启动时会启用会话，进程退出自动冻结导出；宿主也可调用 `TryStopAndWriteConfiguredSnapshot` 提前停止导出。单元测试和临时宿主仍可使用 `Configure(true, 场景名)`/`TryFreezeAndWriteSnapshot`。冻结导出会等待正在写入的指标排空；同一路径并发导出通过临时文件和路径锁保证不会留下半截 JSON。

截至 2026-08-08，仓库没有可驱动 300 个网络连接的既有机器人、测试客户端或负载脚本。`Server/MirEnvir/Robot.cs` 是 NPC 行为脚本机器人，不产生连接规模；`Tests/Base05.Tests` 也没有连接型压测入口。因此下表的“待真实采集”是当前事实，不能用单元测试输出替代。真实 S1/S2/S3 需要服务器资源、账号、地图和独立连接负载入口，已按定义升级列为后续验收项，不阻塞本轮采集基建与正常开发；在资源和入口具备前禁止伪造 JSON 或抄录基线。

## 固定场景定义（依据 PRD §5）

| 场景 | 连接口径 | 活跃口径 | 动作约定 | 状态 |
|---|---:|---:|---|---|
| S1 空闲连接 | 1 | 0 | 保持连接与心跳，记录 10 分钟 | 待真实采集（缺负载入口） |
| S2 活跃战斗 | 100 | 100 | 角色移动、战斗、拾取循环，记录 10 分钟 | 待真实采集（缺负载入口） |
| S3 规划容量 | **300** | **100** | 200 个空闲连接 + 100 个活跃角色，记录 10 分钟 | 待真实采集（缺负载入口） |

三组场景名称和连接/活跃口径固定后，后续压测必须复用相同动作、时长、地图、资源版本和配置，并保留指标 JSON、服务端版本提交号、资源摘要及运行日志。S3 的“300 连接/100 活跃角色”是 PRD §5 的规划测试口径，不代表当前容量已验证。

## 指标字段

`PerformanceMetricKind` 提供 CPU、Update、Draw、DrawCall、TextureSwitch、TextureCreate、Gc、GcGen0、GcGen1、GcGen2、GcPause、Memory、GpuMemory、GpuMemoryBudget、Save、SaveSnapshotCapture、SaveTransactionCommit、SaveFailure、NetworkQueue、NetworkInQueue、NetworkOutQueue、NetworkQueueHighWater、NetworkInQueueHighWater、NetworkOutQueueHighWater、Connections、ActiveConnections、Disconnects 以及移动端 `MobileSpriteBatchBegin`、`MobileSpriteBatchStateChange` 代理字段。耗时通过 `RecordDuration`/`Begin` 采集；队列深度、高水位、连接口径和托管内存通过 `SetGauge`/`SampleRuntime` 采集。`Gc` 与三个代次字段均为会话增量，`GcPause` 是会话内运行时累计暂停时间增量（先将运行时的 TimeSpan ticks 换算为 Stopwatch ticks；运行时不支持时写 `Available=false` 与原因），两者不互相替代。PC 通过现有 DXGI `QueryVideoMemoryInfo` 记录实际本地显存使用量和预算；移动端仍明确写 `Available=false`，不以 0 冒充。p95/p99 使用覆盖完整会话样本的固定 `log2-histogram`，平均值/总量仍覆盖完整会话，结果为可解释的对数桶近似值，JSON 同时记录算法名和样本数；不会在 4096 个样本后只保留最近窗口。调用方应在渲染接缝、主循环、保存事务和网络队列接缝调用，而不是在业务对象中跨线程写玩家状态。

## 采集入口与口径

| 指标域 | 已接入入口 | 口径与限制 |
|---|---|---|
| CPU/Update/Draw | PC `Client_VorticeDX11/Forms/CMain.cs`；移动端 `Client_MonoGame.Shared/CMain.cs`；服务端 `Server/MirEnvir/Envir.cs` | `Begin` 包围同一主循环阶段的墙钟耗时；移动端 `Update` 只包围 `UpdateEnviroment`，输入/UI 的 MonoGame `Update` 不与环境更新混合；三端均不宣称是操作系统进程 CPU 百分比。|
| DrawCall/TextureSwitch | PC `Client_VorticeDX11/MirGraphics/DXManager.cs`；移动端 `Client_MonoGame.Shared/MirGraphics/SpriteBatchStack.cs` | PC 在实际绘制接缝记录调用，并按 D3D 纹理绑定指针记录切换；移动端标准字段写 `Available=false`，仅记录 `MobileSpriteBatchBegin`/`MobileSpriteBatchStateChange` 代理，不把 Begin 冒充 GPU DrawCall 或纹理运行切换。|
| TextureCreate | PC DX11 直接 `CreateTexture2D` 入口；移动端 `MLibrary`、FairyGUI、文本框/场景占位纹理等直接 `new Texture2D`/`FromStream` 入口 | 统计仓库可见的直接创建入口；`Content.Load<Texture2D>` 内部创建由框架管理，当前不宣称已覆盖。|
| GC/Memory | PC/移动端主循环每秒 `SampleRuntime` | `Memory` 为 `GC.GetTotalMemory(false)`；`Gc`/`GcGen0`/`GcGen1`/`GcGen2` 为会话增量；`GcPause` 为两次采样间累计暂停增量。|
| GpuMemory | PC `CMain` + `DXManager.TryGetGpuMemoryUsage`；移动端 `CMain` | PC 每秒通过 DXGI 查询本地段实际使用量与预算；移动端后端不可可靠查询时记录 `Available=false` 和原因，不写 0。|
| Save* | `Server/Persistence/Sql/SqlDomainTransactionRunner.cs` | `Save` 覆盖完整调用（快照、事务、重试和失败）；`SaveSnapshotCapture` 只覆盖快照工厂；`SaveTransactionCommit` 按每次事务尝试记录；失败单独计数。|
| Network*/Connections | 服务端 `Envir` + `MirConnection`；PC/移动端 `MirNetwork/Network` + 主循环 | 入队/出队路径维护深度和连接生命周期高水位，采样即使队列已排空仍保留峰值；服务端网络汇总每秒一次，避免每毫秒 O(连接数) 扫描；服务端连接数为 `Connections.Count`，活跃数为 `Players.Count`；断线在 `MirConnection.Disconnect` 计数。|

性能会话只在环境变量或显式 `Configure(true, 场景名)`/`StartSession(场景名)` 后采样；发布默认关闭。环境变量入口示例：

```text
set LYOCRYSTAL_PERF00_ENABLED=true
set LYOCRYSTAL_PERF00_SCENARIO=S3
set LYOCRYSTAL_PERF00_OUTPUT=artifacts/perf-00/S3-<提交号>.json
```

代码宿主也可在真实场景结束时显式停止。压测驱动负责连接/动作，路径应保存为场景工件：

```csharp
PerformanceMetrics.Configure(enabled: true, scenario: "S3");
// 运行固定场景 10 分钟……
PerformanceMetrics.TryFreezeAndWriteSnapshot(
    "artifacts/perf-00/S3-<提交号>.json",
    out _,
    out var error);
```

若导出失败，必须保留 `error` 和运行日志；不能只保留人工抄录的 p95/p99 数字。环境变量入口仅解决会话启停和导出，不提供 300 连接负载生成能力。

## 自动验证证据（不等同三场景基线）

2026-08-08 在 SDK `10.0.200` 下执行：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~PerformanceMetricsTests|FullyQualifiedName~SqlPersistenceRoundTripTests"
结果：11/11 通过（含全会话 >4096 百分位、环境变量启停导出、队列高水位、GC 代次增量与 SQL 保存失败耗时断言）。
```

测试会在系统临时目录生成并校验 JSON 后清理文件；这些样本用于验证采集接缝和格式，不包含真实网络连接，不能作为 S1/S2/S3 基线。完整测试和平台构建结果由任务提交记录保留。

同日完整测试命令：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo
结果：146/146 通过，0 失败，0 跳过。

同日平台构建：

```text
dotnet build Client_VorticeDX11/Client_VorticeDX11.csproj --no-restore --nologo
结果：0 错误（既有警告）。
dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -f net10.0 --no-restore --nologo
结果：0 错误（既有警告）。
dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -f net10.0-android --no-restore --nologo
结果：0 错误（既有警告）。
```
```

## 真实性与关闭条件

本轮已关闭“指标可采集、会话可导出、口径可解释、自动验证覆盖”的代码退出项；真实 S1/S2/S3 连接型压力基线作为独立后续验收项，当前不阻塞正常开发/GATE-P1 的其余工作。后续需在服务器资源、账号、地图和受控连接负载入口具备后，按 S1/S2/S3 各运行一次真实采集，保存三份 JSON 工件，并在报告中标注运行时长、实际连接数、活跃数、版本与失败/断线计数；不得以“模拟 300 连接”或合成样本声称门禁通过。
