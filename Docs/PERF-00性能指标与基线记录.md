# PERF-00 性能指标与基线记录

## 当前状态

本文件只记录 PERF-00 的采集约定与真实采集状态，不把合成数值当作压测基线。指标接缝已加入共享层，默认关闭。生产进程可设置 `LYOCRYSTAL_PERF00_ENABLED=true`、`LYOCRYSTAL_PERF00_SCENARIO=S1|S2|S3`、`LYOCRYSTAL_PERF00_OUTPUT=<json路径>`，PC、移动端和服务端启动时会启用会话，进程退出自动冻结导出；宿主也可调用 `TryStopAndWriteConfiguredSnapshot` 提前停止导出。单元测试和临时宿主仍可使用 `Configure(true, 场景名)`/`TryFreezeAndWriteSnapshot`。冻结导出会等待正在写入的指标排空；同一路径并发导出通过临时文件和路径锁保证不会留下半截 JSON。

截至 2026-08-08，仓库没有可驱动 300 个网络连接的既有机器人、测试客户端或负载脚本。`Server/MirEnvir/Robot.cs` 是 NPC 行为脚本机器人，不产生连接规模。2026-08-10 产品口径修正为：开发阶段使用隔离 SQLite、自动生成账号和真实 TLS/Packet 协议的模拟客户端做并发压测；不要求几百台真机或生产账号，真机体验仍由用户后续验证。`SimulatedProtocolLoadTests` 已补齐这一入口，并完成 300 连接、100 个已登录协议会话的短时阶段验证。模拟结果只证明服务端协议连接、登录、心跳和补连能力，不冒充真实角色战斗、渲染、设备功耗或 24/72h soak。

## 固定场景定义（依据 PRD §5）

| 场景 | 连接口径 | 活跃口径 | 动作约定 | 状态 |
|---|---:|---:|---|---|
| S1 空闲连接 | 1 | 0 | 保持连接与心跳，记录 10 分钟 | 协议模拟入口已具备 |
| S2 活跃战斗 | 100 | 100 | 角色移动、战斗、拾取循环，记录 10 分钟 | 真实角色动作与真机体验留后续实测 |
| S3 规划容量 | **300** | **100** | 200 个空闲连接 + 100 个登录并持续心跳的协议会话 | 30 秒阶段验证已通过；不是战斗/真机 soak |

三组场景名称和连接/活跃口径固定后，后续实机压测必须复用相同动作、时长、地图、资源版本和配置，并保留指标 JSON、服务端版本提交号、资源摘要及运行日志。本轮 S3 将“活跃”严格限定为完成账号登录并持续发送心跳的协议会话；没有加载地图资源或创建角色对象，因此不能扩张解释为 100 个战斗角色。

## 指标字段

`PerformanceMetricKind` 提供 CPU、Update、Draw、DrawCall、TextureSwitch、TextureCreate、Gc、GcGen0、GcGen1、GcGen2、GcPause、Memory、GpuMemory、GpuMemoryBudget、Save、SaveSnapshotCapture、SaveTransactionCommit、SaveAttemptFailure、SaveFailure、NetworkQueue、NetworkInQueue、NetworkOutQueue、NetworkQueueHighWater、NetworkInQueueHighWater、NetworkOutQueueHighWater、Connections、ActiveConnections、Disconnects 以及移动端 `MobileSpriteBatchBegin`、`MobileSpriteBatchStateChange` 代理字段。耗时通过 `RecordDuration`/`Begin` 采集；队列深度、高水位、连接口径和托管内存通过 `SetGauge`/`SampleRuntime` 采集。`Gc` 与三个代次字段均为会话增量，基线在会话创建时捕获，因此会话开始到首次采样之间的 GC 不会丢失；`GcPause` 是会话内运行时累计暂停时间增量（先将运行时的 TimeSpan ticks 换算为 Stopwatch ticks；运行时不支持时写 `Available=false` 与原因），两者不互相替代。PC 通过现有 DXGI `QueryVideoMemoryInfo` 记录实际本地显存使用量和预算；移动端仍明确写 `Available=false`，不以 0 冒充。p95/p99 使用覆盖完整会话样本的固定四子桶 `log2-sub-bucket-upper-bound` 直方图，代表值取子桶保守上界，最大相对误差上界为 25%，平均值/总量仍覆盖完整会话，JSON 同时记录算法名、误差上界和样本数；不会在 4096 个样本后只保留最近窗口。调用方应在渲染接缝、主循环、保存事务和网络队列接缝调用，而不是在业务对象中跨线程写玩家状态。

## 采集入口与口径

| 指标域 | 已接入入口 | 口径与限制 |
|---|---|---|
| CPU/Update/Draw | PC `Client_VorticeDX11/Forms/CMain.cs`；移动端 `Client_MonoGame.Shared/CMain.cs`；服务端 `Server/MirEnvir/Envir.cs` | `Begin` 包围同一主循环阶段的墙钟耗时；移动端 `Update` 只包围 `UpdateEnviroment`，输入/UI 的 MonoGame `Update` 不与环境更新混合；三端均不宣称是操作系统进程 CPU 百分比。|
| DrawCall/TextureSwitch | PC `Client_VorticeDX11/MirGraphics/DXManager.cs`；移动端 `Client_MonoGame.Shared/MirGraphics/SpriteBatchStack.cs` | PC 在实际绘制接缝记录调用，并按 D3D 纹理绑定指针记录切换；移动端标准字段写 `Available=false`，仅记录 `MobileSpriteBatchBegin`/`MobileSpriteBatchStateChange` 代理，不把 Begin 冒充 GPU DrawCall 或纹理运行切换。|
| TextureCreate | PC DX11 直接 `CreateTexture2D` 入口；移动端 `MLibrary`、FairyGUI、文本框/场景占位纹理等直接 `new Texture2D`/`FromStream` 入口 | 统计仓库可见的直接创建入口；`Content.Load<Texture2D>` 内部创建由框架管理，当前不宣称已覆盖。|
| GC/Memory | PC/移动端主循环每秒 `SampleRuntime` | `Memory` 为 `GC.GetTotalMemory(false)`；`Gc`/`GcGen0`/`GcGen1`/`GcGen2` 为会话增量；`GcPause` 为两次采样间累计暂停增量。|
| GpuMemory | PC `CMain` + `DXManager.TryGetGpuMemoryUsage`；移动端 `CMain` | PC 每秒通过 DXGI 查询本地段实际使用量与预算；移动端后端不可可靠查询时记录 `Available=false` 和原因，不写 0。|
| Save* | `Server/Persistence/Sql/SqlDomainTransactionRunner.cs` | `Save` 覆盖完整调用（快照、事务、重试和失败）；`SaveSnapshotCapture` 只覆盖快照工厂；`SaveTransactionCommit` 按每次事务尝试记录；瞬时重试失败计入 `SaveAttemptFailure`，仅重试耗尽或不可重试异常计入最终 `SaveFailure`。|
| Network*/Connections | 服务端 `Envir` + `MirConnection`；PC/移动端 `MirNetwork/Network` + 主循环 | 入队/出队路径维护会话级逻辑总深度与高水位，方向字段分别维护，不把各连接或各队列的历史峰值相加；新会话首次访问时以当前深度重基线，采样即使队列已排空仍保留本会话峰值；服务端网络汇总每秒一次，避免每毫秒 O(连接数) 扫描；服务端连接数为 `Connections.Count`，活跃数为 `Players.Count`；断线在 `MirConnection.Disconnect` 计数。|

性能会话通常只在环境变量或显式 `Configure(true, 场景名)`/`StartSession(场景名)` 后采样；OPS-BASIC-01 完成后，服务端启用管理 HTTP 时会自动启动不导出的 `server-operations` 会话供基础监控读取，客户端发布默认仍关闭。环境变量入口示例：

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

若导出失败，必须保留 `error` 和运行日志；不能只保留人工抄录的 p95/p99 数字。生产环境变量入口仍只解决会话启停和导出；开发压测由测试项目中的隔离模拟客户端驱动，不接生产账号或生产数据库。

## 自动验证证据（不等同三场景基线）

2026-08-08 在 SDK `10.0.200` 下执行：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~PerformanceMetricsTests|FullyQualifiedName~SqlPersistenceRoundTripTests"
结果：17/17 通过（含全会话 >4096 百分位、子桶误差边界、环境变量启停/关闭导出、跨会话队列高水位、GC 首次采样与 SQL 保存重试计数断言）。
```

测试会在系统临时目录生成并校验 JSON 后清理文件；这些样本用于验证采集接缝和格式，不包含真实网络连接，不能作为 S1/S2/S3 基线。完整测试和平台构建结果由任务提交记录保留。

同日完整测试命令：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo
结果：153/153 通过，0 失败，0 跳过。
```

同日平台构建：

```text
dotnet build Client_VorticeDX11/Client_VorticeDX11.csproj --no-restore --nologo
结果：0 错误（既有警告）。
dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -f net10.0 --no-restore --nologo
结果：0 错误（既有警告）。
dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -f net10.0-android --no-restore --nologo
结果：0 错误（既有警告）。
```

## 模拟并发阶段证据（2026-08-10）

`SimulatedProtocolLoadTests` 启动正式 `Envir.Main` TLS-only 监听，使用临时自签根信任、临时 SQLite 和 100 个自动生成的 Argon2id 账号；300 个客户端均执行正式 `ClientVersion`/Packet 帧，100 个会话完成登录，全部持续心跳，并在主动断开一个会话后自动补足到 300。30 秒稳定窗口结果为：峰值连接 300、登录成功 101 次（含一次补连）、心跳响应 23458 次、建连重试 0、协议失败 0、补连 1、心跳 p95 580.18ms、GC pause p95 19.66ms、网络队列高水位 300。原始 TRX 与复现命令见 `Docs/Evidence/GATE-P4/perf01-02-simulated-load-20260810/`。

## 真实性与关闭条件

本轮已关闭“指标可采集、会话可导出、口径可解释、自动验证覆盖”和 300/100 协议模拟并发退出项。模拟压测不等同真机、真实角色战斗或长时间 soak；用户后续自行实机验证。后续实测仍应记录时长、连接数、活跃角色数、版本与失败/断线计数，并明确区分“模拟协议会话”和“实机角色”。
