# LyoCrystal 三端私服 后续开发设计报告 + PRD 开发文档

| 项目 | 内容 |
|---|---|
| 适用项目 | LyoCrystal（水晶传奇三端版：PC VorticeDX11 / Android+iOS MonoGame+MAUI / Server C# 脚本热更 / 微端更新） |
| 文档版本 | v1.0 |
| 日期 | 2026-08-04 |
| 评审基线 | 仓库当前 master 全量代码审查（PC 82K 行 / 移动端 144K 行 / Server 183K 行 / Shared 18K 行，合计约 43 万行 C#） |
| 读者 | 开发者 luo（单人全职）、后续协作者、维护者 |
| 文档定位 | 设计报告 + PRD 合一。回答：从哪着手、严守哪些边界、优化/修改/新增哪些功能、任务如何分阶段、开发按什么顺序执行 |

---

## 摘要（TL;DR）

**从哪着手**：先修复"工程基座"（sln 无法干净加载、8 个幽灵工程、日志空壳、构建链没闭环），再做一次**移动端 iOS 可玩闭环**补完；随后进入服务端可观测性与性能治理；最后才轮到新玩法功能开发。

**严守的边界**：三端协议一致性（Shared 是唯一事实源，移动端 `Share/` 副本必须回流并逐步改引用）、主循环单线程一致性模型（怪物多线程边界不扩）、脚本只做"旁路 Hook + 数据定义"不重写核心玩法、C# 脚本与 txt 双轨并存的渐进迁移不搞大爆炸、SQLite/MySQL 双方言与 SchemaMigrator 版本化迁移纪律。

**需要优化/修改/新增的功能**（详见第三部分，每条都有风险与工作量）：
- 修：sln/幽灵工程/版本漂移、日志系统、移动端 iOS 空壳、移动端未处理服务端包（~145 个）、PC 双轨更新与死代码、`GameScene`/`PlayerObject` 等巨型单体的模块化入口。
- 优：数据库全量回写改脏标记增量、O(n) 线性查找、渲染合批与图集、移动端资源懒加载预算。
- 新：监控面板（GM Web/仪表盘）、自动化验收（冒烟+回归）、AI 生成脚本强化、微端发布一体化、后续玩法（关系/师徒/坐骑/钓鱼/活动）。

**阶段划分**（详见第四部分）：P0 工程基座（4~6 周）→ P1 移动端补完（6~10 周）→ P2 服务端可靠性与性能（6~10 周）→ P3 自动化验收与可观测性（4~6 周）→ P4 新功能与玩法（8~12 周起，持续迭代）。总计约 28~44 周进入"健康持续迭代"状态。

---

# 第一部分 项目现状与健康度评估

## 1.1 一句话画像

LyoCrystal 是一个在经典 Mir2 单线程私服架构之上完成了"现代化改造"的三端项目：PC 端渲染升级到 VorticeDX11、废弃二进制库迁移 SQLite/MySQL、服务端引入 Roslyn C# 脚本热更与调试、移动端（Android+iOS）用 MonoGame+MAUI 实现三端互通、并实现了微端（分包增量）资源更新。**功能面上已经是一个"能玩"的三端私服，但工程面上处于"能用但很难继续加东西"的状态。**

## 1.2 子系统规模与角色

| 子系统 | 目录 | 规模 | 目标框架 | 健康度评级 |
|---|---|---|---|---|
| PC 客户端 | `Client_VorticeDX11/` | 109 文件 / 82K 行 | net8.0-windows | ★★☆☆☆ 可玩，架构极重 |
| 移动端共享 | `Client_MonoGame.Shared/` | 250 文件 / 144K 行 | net8.0+net11.0-android+ios | ★★★☆☆ 核心可玩，iOS 空壳 |
| 移动端壳 | `Client_MonoGame.Android/iOS/` | 11 文件 / 0.5K 行 | net11.0-android/ios | ★★☆☆☆ Android 可用，iOS 占位 |
| 服务端核心 | `Server/` | 464 文件 / 133K 行 | net8.0 | ★★★☆☆ 功能全，债集中 |
| 服务端管理端 | `Server.MirForms/` | 77 文件 / 50K 行 | net8.0-windows | ★★★☆☆ 强大但巨型 |
| 共享协议层 | `Shared/` | 19 文件 / 18K 行 | net6.0;net8.0 | ★★★★☆ 干净叶子节点 |
| 工具链 | `Tools/` `AutoPatcherAdmin/` `LibraryEditor/` 等 | ~12K 行 | 混合 | ★★☆☆☆ 一半是幽灵 |
| 文档 | `Docs/` | 11 文件 | — | ★☆☆☆☆ 原始 dump，缺架构说明 |

## 1.3 关键结论（先结论后依据）

1. **三端协议是互通的，但移动端协议层已经"复制分叉"**：PC 与移动端的 `ServerPackets.cs` 逐字节一致，但移动端 `Share/Enums.cs` 已删改（WeatherSetting、Stat、LevelEffects），且移动端不引用根 `Shared` 而是维护一份 `Share/` 副本。**这是未来三端加功能时最容易踩的坑。**（证据：`Client_MonoGame.Shared/Share/` 与 `Shared/` 对应文件 diff；移动端 csproj 不引用 `Shared.csproj`）

2. **服务端脚本热更系统是全场最大的加分项，设计完成度高**：Roslyn 动态编译 + 可回收 ALC 热替换 + FileSystemWatcher/手动推送双通道 + 断点/单步/表达式调试 + OpenAI 兼容 AI 生成 + txt 渐进退役策略。**但它覆盖的是"旁路 Hook + 数据定义"，核心玩法仍是 C# 硬编码。**（证据：`Server/Scripting/` 全目录）

3. **移动端 iOS 是空壳**：`net11.0-ios` TFM 分支只编译 `Compat/` 存根（46 行日志类），缺 MonoGame.Framework.iOS 与 FontStashSharp 引用，AOT 配置未作用到真游戏。**"三端互通"实际是"Android + PC"互通。**（证据：`Client_MonoGame.iOS.csproj` + `Client_MonoGame.Shared.csproj` 的 iOS 条件编译）

4. **PC 客户端是巨型单体**：`GameScene.cs` 13,174 行（含 MapControl 多类）、`MonsterObject.cs` 8,136、`PlayerObject.cs` 5,787，几十个静态可变单例，无 DI，无单元测试。**任何新增系统都要靠复制粘贴巨型文件推进。**

5. **服务端持久化有明确的扩展性风险**：每 5 分钟主线程同步执行"整库全删重插"，玩家/物品越多越卡；大量 `GetMap/GetMonsterInfo/GetGuild` 是 O(n) 线性扫描；`MessageQueue` 超 100 条直接丢消息、`Logger` 整体注释为空壳。

6. **工程基座不健康**：`Legend of Mir.sln` 引用 8 个磁盘上不存在的工具工程与 4 个缺失的 Note 文件，**解决方案无法干净加载**；`global.json` 钉死 preview SDK；Roslyn 版本两端漂移（4.10 vs 4.12）；`Tools/` 下 9 个命名工具只有 `MobileBootstrapAudit` 真实存在。

7. **没有自动化测试**：全仓无任何 `[Test]`/测试项目；服务端的验证完全依赖 `ScriptDebugForm` 的在线 TestBench 手动触发。

## 1.4 现状数据锚点

- PC 端渲染接缝：`Client_VorticeDX11/MirGraphics/DXManager.cs`（2,308 行），所有绘制最终走 `DXManager.Draw/DrawOpaque/DrawMultiply`；每张带色图新建 `ColorMatrixEffect`、每帧 `CreateSharedBitmap` 是热路径成本点。
- 服务端脚本编译：`Server/Scripting/ScriptCompiler.cs`（Roslyn `CSharpCompilation`），热更 `ScriptWatcher.cs`，调试 `Server/Scripting/Debug/ScriptDebugInstrumenter.cs`。
- 服务端持久化：`Server/Persistence/Sql/SqlServerPersistence.cs`（6,860 行）+ `SchemaMigrator.cs`（内嵌 17 个 SchemaMigration）+ `SqliteDialect/MySqlDialect` 双方言。
- 微端更新：移动端 `Client_MonoGame.Shared/BootstrapPackage*.cs`（~3.6K 行）+ PC 端 `Client_VorticeDX11/Bootstrap/PcBootstrap*.cs`（~1.2K 行），同构镜像；发布链依赖 `Tools/Mobile-BootstrapPackageRepoExport.ps1`。
- 移动端 FairyGUI：`Client_MonoGame.Shared/UI/FairyGui/`，内嵌移植运行时（~172 文件）+ 宿主 `FairyGuiHost.cs`（11,661 行）+ 28 个 `Mobile*.cs` 窗口分部类。

---

# 第二部分 开发边界（红线）

> 这是"继续开发必须严守"的纪律。违反任何一条，后续成本会指数上升。

## 2.1 协议层边界：Shared 是唯一事实源

- **绝对不许**在移动端 `Share/` 副本里"顺手改协议"，也不许在 PC/移动端各自扩展 `ServerPacketIds`。
- 任何新功能涉及的新包/新字段/新枚举，**必须先改 `Shared/`，再回流移动端 `Share/`**（或直接让移动端改引用根 Shared——见 P0-3）。
- 服务器 `MirConnection.ClientVersion` 的 `VersionHashes` 校验是跨端兼容的守门人，协议变更必须同步更新版本哈希。
- 违反后果：三端互踢、版本哈希不匹配、线上用户无法更新。

## 2.2 主循环边界：单线程一致性模型

- 服务器主循环是**单线程游戏状态 + 怪物分线程**模型（`Envir.WorkLoop` + `MobThread[]`）。玩家/Hero/NPC/宠物等非怪物对象**只在主线程处理**。
- 怪物线程与主线程的共享状态（`Master`、战斗数据）目前靠"pets 不并行处理"的注释约定规避竞争。**新增跨线程访问时必须回到主线程**（走 `InvokeOnMainThread`），不许在怪物线程里直接读写玩家状态。
- 违反后果：幽灵级偶发崩溃/数据错乱，极难复现。

## 2.3 脚本边界：旁路 Hook + 数据定义，不重写核心玩法

- C# 脚本体系的定位是"给策划/运营的旁路 Hook + 数据定义"（NPC 页、任务定义、掉落、配方、经济费率、名单/路线），**不是**重写怪物 AI/技能/拍卖/攻城战主流程的通道。
- 新增玩法先问：这是"数据/策略"还是"引擎逻辑"？前者进脚本（ScriptApi/Registry），后者进 C# 核心并暴露 Hook。
- 违反后果：脚本层侵入核心后，热更一个脚本可能把整服逻辑拖垮，且无法调试。

## 2.4 迁移边界：渐进双轨，不搞大爆炸

- 脚本迁移：C# 与 legacy txt 双轨并存，由 `TxtFallbackPolicy`/`ScriptDispatchPolicy` 逐步切换。**一次只迁一批 NPC/任务，每批过 `NpcScriptCoverage` 覆盖率门槛再切下批。**
- 数据库：SQLite/MySQL 双方言 + `SchemaMigrator` 版本化迁移。**改表结构必须新增一个 `SchemaMigration`，禁止手改库。**
- 违反后果：txt→C# 切换事故、库结构漂移、玩家数据损坏。

## 2.5 渲染层边界（PC 与移动端各自收敛）

- PC：所有绘制走 `DXManager` 接缝；移动端：所有绘制走 `SpriteBatchStack`。**禁止在业务代码里绕过接缝直连 D3D/GPU 对象。**（目前两者基本遵守，需维持）
- 移动端贴图创建有"帧内预算"（`MobileMaxTextureCreatesPerFrame`，防掉线），**新增资源加载不得破坏预算**。

## 2.6 工程卫生边界

- **sln 与磁盘必须同步**：新增/删除工程必须同步 `Legend of Mir.sln`（当前已失同步，P0 修复）。禁止再产生"幽灵工程"。
- 版本统一：`global.json` SDK、Roslyn（4.10/4.12 二选一）、MonoGame（3.8.1）、FontStashSharp 版本集中到 props/中央配置。
- 配置不得硬编码环境：`Settings.cs` 里 `192.168.0.100`、`ftp://`、`@123456` 等必须外置到配置文件/环境变量（P0 处理）。
- 违反后果：新协作者无法构建、构建产物随机器漂移、安全隐患。

## 2.7 测试边界：先有观测再有断言

- 服务端/客户端改动至少保持"可冒烟"：服务端改动必须能通过离线冒烟（P3 补自动化，当前靠 `ScriptDebugForm` TestBench）。
- 破坏性重构（协议、持久化、脚本 API）必须有对应的数据迁移脚本 + 回归清单，**禁止裸改**。

---

# 第三部分 需要优化、修改、新增的功能（分优先级）

> 优先级定义：**P0** 不修后面全堵（基座/一致性）；**P1** 直接影响"继续开发"主目标（移动端补完/服务端可靠性）；**P2** 提效与质量；**P3** 新玩法/锦上添花。每条含：为什么、做什么、风险/工作量。

## 3.1 工程基座修复（P0）

### P0-1 解决方案与幽灵工程清理
- **现状**：sln 引用 8 个不存在的工程 + 4 个缺失 Note 文件；`Tools/` 下 9 个命名工具仅 `MobileBootstrapAudit` 存在。
- **做**：从 sln 移除幽灵工程（或把一次性脚本工具源码补回 `Tools/` 归档）；恢复/删除缺失 Note；清理 `Components/` 的 PowerPacks 等停产物件；给 `Utils`/`ServerFolder` 分组改名或移除。
- **验收**：克隆后 `dotnet build Legend of Mir.sln` 零错误零警告（或明确的白名单警告）。
- **工作量**：0.5~1 周。

### P0-2 日志系统恢复
- **现状**：`Logger.cs` 整体注释为空壳，`MessageQueue` 超 100 条直接丢弃，生产日志大量丢失；`Reporting.cs` 的 `[CallerMemberName]` 追踪指向空实现。
- **做**：恢复/重写 `ILog` 门面（log4net 或 Serilog），保留内存队列但改为**环形有界 + 落盘 + 关键级别绝不丢弃**；把 `MessageQueue` 超限策略改为降级写盘而非丢弃；接入 `SqlSaveResilience` 事件与 `ScriptDebug` 轨迹。
- **验收**：崩溃现场有最近 N 分钟全量日志；脚本异常/保存失败有结构化日志可查。
- **工作量**：1~1.5 周。

### P0-3 移动端协议层回流：Share → Shared
- **现状**：移动端 `Client_MonoGame.Shared/Share/` 是 `Shared/` 的复制副本，`Enums.cs` 已分叉。
- **做**：把根 `Shared/` 设为目标 `net8.0;net11.0-android;net11.0-ios` 多目标（或抽一个"协议核心"子项目），移动端 csproj 改为引用根 `Shared`（命名空间差异用 `global using` 桥接，或一次性把 `MonoShare` 别名并回 `Shared`）；同步合并 `Enums.cs` 分叉（以根 Shared 为准，逐项确认移动端删改是否是有意的移动端行为差异——若是，用条件编译而非改协议）。
- **验收**：`git diff Shared/ Share/` 为空；移动端/PC/Server 三端同一次协议变更在同一 PR 内完成。
- **风险**：移动端命名空间改动波及 144K 行；建议用**引用替换 + 双命名空间兼容期**降低一次性风险。
- **工作量**：2~3 周（含分叉审查与兼容期）。

### P0-4 构建链闭环与版本统一
- **现状**：`global.json` 钉死 `11.0.100-preview.2`；Roslyn 4.10/4.12 漂移；`#if Windows` 符号未定义（FairyGUI 剪贴板走错分支）；`MauiVersion` 依赖环境注入。
- **做**：SDK 升到稳定 net11（或回退到 team 都装的稳定版）；Roslyn 统一到 4.12；给 csproj 补上 `Windows` 等符号；把 `MauiVersion`/MonoGame/FontStashSharp 版本集中到 `Client_MonoGame.Mobile.props`；补一份 `BUILD.md`（PC 构建 / Android AOT / iOS / 微端导出的逐步命令）。
- **验收**：按 `BUILD.md` 从干净 clone 依次构建 PC/Android/Server 全绿；iOS 目标在 macOS 环境可编译（见 P1-1）。
- **工作量**：1~2 周。

### P0-5 配置外置与安全底线
- **现状**：`Settings.cs` 硬编码 `IPAddress=192.168.0.100`、`ftp://192.168.0.100:8888/`、`MicroBaseUrl=http://192.168.0.100:7777/api/`、默认 `GMPassword="@123456"`。
- **做**：IP/端口/BaseUrl/GM 密码迁移到 `Setup.ini`/环境变量；加启动期校验（缺失给出明确提示）；GM 密码要求非默认才可启动服务端（或启动时打强警告）。
- **验收**：新部署无需改代码即可改 IP；默认弱密码无法启动（除非显式 `--allow-default-gm`）。
- **工作量**：0.5~1 周。

## 3.2 移动端补完（P1）

### P1-1 iOS 端从空壳到可玩
- **现状**：`net11.0-ios` 只编 `Compat/` 存根；缺 `MonoGame.Framework.iOS` 与 FontStashSharp 引用；`IosBootstrapGame.cs` 无人引用。
- **做**：补齐 iOS 引用 → 让 iOS TFM 编译真实游戏场景（先关掉 `Compat` 条件编译，排除平台无关代码）→ 在 macOS+iOS workload 上跑通构建 → 真机验证登录→游戏场景→地图/对象渲染 → FairyGUI 输入（`#if Windows` 分支的剪贴板/IME 给 iOS 适配）→ AOT/LLVM 配置落到真实目标。
- **验收**：iPad/iPhone 真机从登录到进图打怪，协议与 PC 互通；断点续传微端可用。
- **风险**：iOS 是最大未知（无 macOS 环境则本项阻塞）；工作量按**真机可用**口径，若只要求可编译则减半。
- **工作量**：4~6 周（真机）；2~3 周（仅编译闭环）。

### P1-2 移动端服务端包补齐（~145 个未处理包）
- **现状**：`GameScene/LoginScene/SelectScene/MirScene` 合计处理约 234/379~453 个 `ServerPacketIds`，未处理约 145 个，涉及婚姻/英灵/物品封印/租赁/钓鱼/活动/坐骑/智能宠物/商店购买等。
- **做**：按"发送侧有触点 → 接收侧补 case → 最小 UI"顺序逐系统补齐。优先补**与 PC 对齐且玩家高频**的：商店 `GameShopBuy`（现只有列表）、关系/师徒窗口（FairyGUI 占位已留 TODO）、坐骑（`MountDialog` 已注释）、物品封印/租赁。
- **验收**：每个补完系统：PC 与移动端行为一致、无 `未处理包` 告警。
- **工作量**：3~5 周（按系统分批）。

### P1-3 移动端渲染合批与图集
- **现状**：地图逐 cell、对象逐精灵 `Draw/DrawBlend`（每个 `DrawBlend` 一次 blend 状态切换），`SpriteBatchStack` 只做嵌套 Begin/End 去重，无纹理排序/图集；无粒子系统。
- **做**：为地图静态层建立"已展开的静态精灵缓存"（进入地图时把 Floor/Background 展平成大图/图集，运行时单批绘制）；对象层做纹理排序 + 合批；粒子系统按 PC 版移植（移动端已删 `Particles/`）。
- **验收**：同屏 100+ 对象/大场景 FPS 对比提升 ≥40%（Android 中端机）；draw call 计数下降 50%+。
- **风险**：静态展平与地图缩放/光照（`DrawBlend`）叠加时可能破坏视觉；需做 diff 截图对比。
- **工作量**：3~4 周。

## 3.3 服务端可靠性与性能（P1/P2）

### P1-4 数据库增量回写（替换全量快照）
- **现状**：每 5 分钟主线程同步整库全删重插（账号域 20+ 表 + 物品域全量）。
- **做**：让 `EntityChangeTracker` 真正驱动增量写：脏实体标记 → 按实体 Upsert；把 Save 移到后台线程 + 主线程只做队列交接；`SqlSaveResilience` 熔断保留。
- **验收**：万级在线（或万件物品）下 5 分钟保存不再卡主循环（保存耗时曲线进入后台）；崩溃最多丢变更窗口内的**增量**而不是整域。
- **工作量**：3~4 周。

### P1-5 热点查找索引化
- **现状**：`Envir.GetMap/GetMapInfo/GetMonsterInfo/GetPlayer/GetItemInfo/GetGuild` 全为 O(n) 线性扫描。
- **做**：按 ID 建 `Dictionary<int,...>` 索引（启动时构建，改动时同步）；`LegacyItemNameAliases` 中英文别名表改为启动一次性加载的字典。
- **验收**：`GetPlayer(int)` 均摊 O(1)；启动时间无显著劣化。
- **工作量**：1~2 周。

### P1-6 网络层与 GC 压力
- **现状**：`MirConnection.Process` 用 `List<byte>+AddRange` 逐包拼接；客户端发送侧同款。
- **做**：发送缓冲改为池化 `ArrayPool<byte>`/预分配；批发送攒够再 flush。
- **验收**：高峰发送 GC 分配下降（perf counter 或 ETW 采样）。
- **工作量**：1 周。

### P2-1 怪物多线程数据竞争治理
- **现状**：怪物线程与主线程共享 `Master`/战斗数据仅靠约定；`PlayerObject/HumanObject` 0 处 lock。
- **做**：将"怪物涉及玩家的读"收敛到主线程快照/事件；为跨线程读的玩家字段做原子化或版本号；保留单线程假设的前提下加断言（debug 下校验"只能主线程写玩家"）。
- **验收**：长时间压测（机器人+玩家混跑）无幽灵崩溃；开启断言无违规。
- **工作量**：2~3 周。

## 3.4 自动化验收与可观测性（P2）

### P2-2 离线冒烟与协议回归
- **现状**：零测试；`Tools/ServerSmokeTest` 是幽灵工程。
- **做**：补一个可离线的 ServerSmokeTest（启动无头服务端→加载世界→模拟登录→跑几个核心流程→断言协议往返）；把协议序列化测试做成数据驱动（用 `Shared/` 的 `Packet` 做 golden 序列化/反序列化测试）。
- **验收**：CI 或本地一键脚本跑绿；每次协议变更自动校验 PC/移动/Server 三端序列化一致。
- **工作量**：2~3 周。

### P2-3 监控面板（GM Web 仪表盘）
- **现状**：无 REST 管理 API；微端 HTTP 服务只有 `/api/health` `/api/file` 等。
- **做**：在 `Utils/HttpServer.cs` 基础上加只读监控端点：在线/地图分布/保存耗时/脚本热更状态/消息队列积压；前端一个极简 HTML 仪表盘（可复用 `PatcherWebSite` 前端壳）。
- **验收**：开服后运营/开发者可看核心指标；鉴权走 GM 口令。
- **工作量**：2~3 周。

### P2-4 脚本热更体系加固
- **现状**：`Docs/Scripting/ScriptManual.md`、`KeySpec.md` 缺失（AI 生成的文档上下文缺失）；AI 输出安全守卫存在但无覆盖测试。
- **做**：补全脚本文档（ScriptManual + KeySpec + 常见脚本模板）；`AiScriptOutputGuard` 加单元测试（危险 API 黑名单回归）；脚本热更失败保留旧版已有，补"热更后自动跑冒烟脚本"。
- **工作量**：1~2 周。

## 3.5 代码卫生（P2）

- **巨型单体入口模块化**：给 `GameScene.cs`（13K 行）、`PlayerObject.cs`（16K 行）、`HumanObject.cs`（9.4K 行）、`SqlServerPersistence.cs`（6.9K 行）**先做"只读边界化"**：抽分区（partial class）或按领域拆出只读访问器，不改变行为——**以"能单独打开+能 grep 定位领域"为目标，不做行为重构**。
- **死代码与双轨清理**：PC 端旧 AutoPatcher/PList 链路、`PatcherWebSite` 死代码、`Settings.ResourcePath=".\DirectX\"` 陈旧命名、sln 里失效 OSX/Linux 条件路径——归档或删除并同步 sln。
- **中文文案外置**：PC 对话框里硬编码中文逐步迁到 `GameLanguage`。
- **工作量**：上述每项 0.5~2 周，**穿插在 P1/P2 之间做，不做专门大重构阶段**。

## 3.6 新功能与玩法（P3，做不做的"产品候选清单"）

> 这些是"继续开发"的方向性增量，非本期必做。按产品价值排序：

1. **关系/师徒/坐骑/钓鱼/活动等 PC 已有、移动端缺的系统**：协议与逻辑已在 Server/PC 存在，移动端只差 UI+绑定（P1-2 已覆盖主体）。这是性价比最高的"新功能"。
2. **移动端离线挂机/双端同进度**：手机端补"离线收益 + 上线结算"，提升留存。
3. **AI 脚本生成升级**：把 `ScriptGenerationService` 从"生成草稿"推进到"按意图补全/审查已有脚本"，内置模板库补齐 `Docs/Scripting`。
4. **微端发布一体化**：把 `MobileBootstrapAudit` + 导出 PS1 + `AutoPatcherAdmin` 收敛成"一条命令出全平台发布包 + 版本索引 + 校验"，集成到 CI。
5. **新玩法模块**（若要做）：建议优先"世界 BOSS 活动 + 跨服聊天/排行"，协议增量最小。
6. **PC 端功能对齐**：PC 端没有而移动端有的（如双指缩放、FairyGUI 新 UI 风格）可按需反向移植。

---

# 第四部分 任务分阶段与开发顺序

> 原则：**先基座、再移动端主目标、再服务端可靠性、再自动化、最后玩法**。单人全职口径，含每阶段"退出条件/Go-No-Go 门禁"。

## 阶段总览（估算 28~44 周进入健康持续迭代）

| 阶段 | 名称 | 时长 | 关键产出 | 门禁 |
|---|---|---|---|---|
| P0 | 工程基座修复 | 4~6 周 | sln 可构建、日志可用、协议回流、配置外置 | 干净 clone 全绿构建 |
| P1 | 移动端补完 | 6~10 周 | iOS 可玩闭环、未处理包补齐、渲染优化 | iOS 真机进图；移动端无未处理包 |
| P2 | 服务端可靠性与性能 | 6~10 周 | 增量保存、索引化、线程治理、监控 | 压测无幽灵崩溃、保存不卡主循环 |
| P3 | 自动化验收与可观测性 | 4~6 周 | 冒烟/回归、监控面板、脚本文档 | CI 绿、故障可回溯 |
| P4 | 新功能与玩法 | 8~12 周起 | 关系/师徒/坐骑/钓鱼等 + 产品增量 | 每功能过验收清单 |

> 说明：P0/P1/P2 允许部分并行（如 P0-3 协议回流是 P1 的前置，必须串行；P0-4 构建链与 P1-1 iOS 可并行）。单人全职建议严格串行，除非你有稳定协作者。

## 阶段 P0：工程基座修复（4~6 周）

目标：让"继续开发"这件事从"每个新改动都踩基座地雷"变成"可以安全加功能"。

| 周 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| 1 | P0-1 sln 清理、P0-4 版本统一 | — | `dotnet build` 干净 |
| 2 | P0-2 日志系统恢复 | P0-1 | 日志落盘可查、队列不丢弃 |
| 2~4 | P0-3 移动端协议回流（含分叉审查） | P0-4 | `diff Shared/ Share/` 为空 |
| 3~4 | P0-5 配置外置、GM 密码守卫 | P0-4 | 新部署零改码；弱密码被拦 |
| 5~6 | 协议变更后三端回归（补 P2-2 前置的协议测试最小集） | P0-3 | 协议变更三端一键验证 |

**门禁（Go/No-Go）**：干净 clone 按文档构建 PC/Server/Android 全绿；`git diff Shared/ Share/` 为空；日志系统可用。**不满足则不得进入 P1。**

## 阶段 P1：移动端补完（6~10 周）

目标：移动端从"Android 可玩"到"双端真正互通 + 高频系统无缺口"。

| 周 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| 1~3 | P1-1a iOS 编译闭环（引用补齐、兼容分支去除、构建打通） | P0-3/4 | macOS 环境 iOS Release 构建通过 |
| 4~6 | P1-1b iOS 真机验证（登录→进图→FairyGUI 输入→微端） | P1-1a | 真机完整玩法链路可用 |
| 3~7 | P1-2 服务端包补齐（分批：商店→关系/师徒→坐骑→封印/租赁） | P0-3 | 高频系统无"未处理包" |
| 7~10 | P1-3 渲染合批与图集（静态层展平→对象合批→粒子） | P1-2 | 大场景 FPS +40%、draw call -50% |

**门禁**：Android+iOS 均可登录进图打怪；移动端高频系统与 PC 行为一致；渲染优化不破坏视觉（截图 diff 通过）。

## 阶段 P2：服务端可靠性与性能（6~10 周）

目标：服务端从"能跑"到"能扛 + 能查"。

| 周 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| 1~4 | P1-4 数据库增量回写（后台化 + 脏标记） | P0-2 日志（先有观测） | 万件物品保存不卡主循环 |
| 4~6 | P1-5 热点索引化 + P1-6 网络 GC 优化 | P1-4 | O(1) 查找、发送 GC 下降 |
| 6~9 | P2-1 怪物多线程治理 + 断言 | P1-5 | 压测无幽灵崩溃 |
| 9~10 | P2-3 监控面板（只读指标 + 仪表盘） | P0-2 | 运营可看在线/保存/队列指标 |

**门禁**：机器人+玩家混跑压测 72h 无崩溃、无数据损坏；保存/查找不再构成瓶颈；监控面板上线。

## 阶段 P3：自动化验收与可观测性（4~6 周）

目标：把"靠人肉 TestBench"变成"可重复的回归资产"。

| 周 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| 1~3 | P2-2 离线冒烟 + 协议 golden 回归（覆盖 Shared 三端一致） | P0-3 | 一键脚本绿；协议变更自动检测 |
| 3~5 | P2-4 脚本文档补全 + AI 守卫单测 | P2-2 | ScriptManual/KeySpec 可用；守卫回归绿 |
| 5~6 | 冒烟接入发布链（微端导出前后各跑一次） | P2-2 | 发布 = 构建+冒烟+导出 一条命令 |

**门禁**：CI（或一键脚本）覆盖 服务端启动冒烟 + 协议回归 + 微端导出；任一变灰立刻拦发布。

## 阶段 P4：新功能与玩法（8~12 周起，持续迭代）

> 每期独立立项，走统一迭代模板：需求 → 协议（先 Shared）→ 服务端逻辑/Hook → PC/移动端 UI → 三端回归 → 发布。

建议第一候选：**关系/师徒/坐骑/钓鱼/活动**（协议与 Server 已有，纯 UI+绑定补完），单系统 1~2 周即可上线。之后按产品数据决定下一批。

---

# 第五部分 开发执行原则（DoD 与纪律）

## 5.1 每个任务的完成定义（Definition of Done）

- 协议改动：`Shared/` 为源 → 移动端回流 → 版本哈希更新 → 三端序列化回归绿。
- 功能改动：PC 与移动端行为一致（同一验收脚本）；无新增"未处理包"告警。
- 服务端改动：过离线冒烟；保存/日志有结构化输出；无新增 O(n) 热路径。
- 任何改动：sln 同步、版本不漂移、无新增 TODO/占位（或占位有 Issue 编号）。

## 5.2 每日/每周节奏（单人全职）

- **每日工件检查**：今天产出是"用户能看到的工件"（可跑代码/截图/对比数据）还是"过程资产"（分析器/矩阵/工具）？**后者连续超过 1 天要降级。**
- **每周**：三端各出一个可运行构建 + 一个验收截图；更新一份 `STATUS.md`（当前阶段/门禁状态/风险）。

## 5.3 风险登记（当前已知）

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| iOS 无 macOS 环境，真机验证受阻 | 中 | 高 | P1-1a 先做编译闭环，真机作为可选门禁 |
| 移动端协议回流 144K 行命名空间波及 | 高 | 中 | 双命名空间兼容期 + 引用替换分步走 |
| 增量回写改崩存盘点 | 中 | 高 | 保留全量快照为 fallback + 灰度开关 |
| 渲染图集/静态展平破坏视觉 | 中 | 中 | 截图 diff 对比 + 可开关的特性开关 |
| 单人全职并行任务过多 | 高 | 高 | 严格串行 P0→P1→P2→P3，P4 才允许小并行 |

---

# 附录 A：快速参考——关键文件地图

| 关注点 | 文件 |
|---|---|
| PC 入口/主循环 | `Client_VorticeDX11/Program.cs`、`Forms/CMain.cs` |
| PC 渲染接缝 | `Client_VorticeDX11/MirGraphics/DXManager.cs` |
| PC 游戏主场景 | `Client_VorticeDX11/MirScenes/GameScene.cs`（13K 行） |
| 移动端入口 | `Client_MonoGame.Android/MainActivity.cs`、`Client_MonoGame.Shared/CMain.cs` |
| 移动端 FairyGUI 宿主 | `Client_MonoGame.Shared/UI/FairyGui/FairyGuiHost.cs` + `Mobile*.cs` |
| 移动端微端 | `Client_MonoGame.Shared/BootstrapPackage*.cs`、`MirGraphics/MicroLibraryHelper.cs` |
| 服务端主循环 | `Server/MirEnvir/Envir.cs` |
| 服务端网络 | `Server/MirNetwork/MirConnection.cs` |
| 服务端持久化 | `Server/Persistence/Sql/SqlServerPersistence.cs` + `SchemaMigrator.cs` |
| 脚本热更 | `Server/Scripting/ScriptManager.cs`、`ScriptCompiler.cs`、`ScriptWatcher.cs` |
| 脚本调试 | `Server.MirForms/Systems/ScriptDebugForm.cs` |
| 协议层 | `Shared/Packet.cs`、`ClientPackets.cs`、`ServerPackets.cs` |
| 微端发布链 | `Tools/Mobile-BootstrapPackageRepoExport.ps1`、`Tools/MobileBootstrapAudit/` |
| 构建配置 | `Client_MonoGame.Mobile.props`、`Client_MonoGame.Mobile.BootstrapShell.targets` |

# 附录 B：移动端未处理服务端包清单（抽样，按优先级）

来源：`GameScene/LoginScene/SelectScene/MirScene` 的 `ProcessPacket` case 与 `ServerPacketIds` 比对。

- **高频/玩家直接可感知**：`GameShopBuy`（商店购买无 UI）、婚姻系（`Marriage/Divorce/MarriageReply/ChangeMarriage`）、坐骑 `MountUpdate` 之外的交互、物品封印/租赁（`ItemSealChanged/ItemRentalLock*`）、师徒 `HeroInformation/HeroBaseStatsInfo/ChangeHero/ManageHeroes`。
- **中频**：`FishingCast/FishingChangeAutocast`、活动/赏金相关、`CanActivateBuff/CanAlterAlliance/CapturePalace`。
- **低频/完整功能缺**：`MarketSearch/MarketBuy`（发送侧有触点但接收侧无 case）。

> 完整清单需在 P1-2 立项时用脚本自动比对生成（把 `ServerPacketIds` 枚举与各 `ProcessPacket` 的 case 集合做差集），当前先以抽样定位。

---

*本报告基于 2026-08-04 仓库全量代码审查。文档中涉及的"工作量"为单人全职估算；若并行协作者增加，P0/P1 可按阶段拆开并行（注意 P0-3 是 P1 的硬前置）。*
