# PRD：PC 稳定与 Android 正式发布

| 项目 | 内容 |
|---|---|
| 适用项目 | LyoCrystal（水晶传奇三端版） |
| 文档版本 | v1.5（2026-08-08 实际进度同步） |
| 日期 | 2026-08-08 |
| 目标 | 本轮：**PC 稳定 + Android 正式发布**。iOS 仅隔离不阻塞，后置 |
| 读者 | 开发者 luo + 并行开发会话 |
| 关联 | 架构事实/边界/ADR 见《继续开发架构设计报告》；**执行纪律见《执行纪律与防走偏铁律》** |

> 生命周期状态：**已完成并转入维护**。本 PRD 保留 P0～P5 的产品目标、验收结果和历史任务快照；文中的“当前”“进行中”“仍未完成”“保持开启”和滚动预测均是对应日期的过程记录，不代表仓库现状。2026-08-12 起，工程治理状态只在《工程治理实施路线》维护；新的产品工作必须另行指定活动 PRD、Issue 或实施规格，不得把本文件 §4.4 或 P6/P7 路线图直接解释为当前工作队列。

---

## 1. 用户与运营目标

| # | 目标 | 度量 |
|---|---|---|
| G1 | PC 端保持稳定可玩 | 无阻塞级功能回归；24~72h 压测无幽灵崩溃 |
| G2 | Android 正式发布官网 APK | 签名 APK 可安装；资源清单已签名；可灰度/回滚 |
| G3 | **PC + Android** 协议一致不漂移 | `Shared.Protocol` 唯一源 + 自动生成 manifest；协议回归自动化 |
| G4 | 数据可恢复 | SQLite WAL + 单写者 + 备份/恢复演练；RPO ≤ 5min（配置强校验）、RTO ≤ 30min |
| G5 | 公开前安全达标 | 凭据不通过未加密网络传输；无弱哈希；无默认 GM 密码；管理端独立凭据 |
| G6 | 移动端功能闭环 | 目标功能按设备实操通过；外部条件卡住时按 §2 的真实协议/门控/权威响应/UI 投影使用探针通过 |
| G7 | 构建可复现 | 从指定源码提交 + 资源获取脚本后，**本机**可复现构建（BASE-02）；CI 裸克隆可重建（BASE-02b）为独立后续项，不阻塞本轮 |
| G8 | 发布前必备运营能力 | 最小监控/崩溃诊断/Kill Switch/授权审计**有实施任务与验收**，P5 门禁前完成 |
| G9 | 执行防走偏 | 每阶段产出以"工件"（代码/diff/截图/数据）为主；过程资产（分析/计划/矩阵）占比 < 20%；命中停止条件即停下报告，不换更大工具打转（见《执行纪律与防走偏铁律》） |

**明确非目标（本轮不做）**：

- iOS 真机可玩 / iOS 完整接入（只保证不阻塞 Windows/Android 构建）
- 跨服聊天 / 跨服排行 / 离线挂机（改变经济/留存逻辑，且与单服 SQLite 规模不匹配）
- 粒子系统完整移植（由实际画面需求驱动）
- MySQL 生产切换（只在触发门槛后立项）
- 每周"三端构建"门禁（本轮门禁 = Windows + Android，iOS 非阻塞）
- 依赖 .NET 11 Preview 的任何发布物

---

## 2. 功能闭环矩阵（移动端）

> 依据审计：不是"补齐所有未处理包"，而是"目标功能闭环通过"。状态分为代码/自动验证与真机验收两层，禁止用普通单元测试代替设备验收。按 2026-08-09 产品决定，本项目将逍遥模拟器作为 Android 实机等效设备；在逍遥上完成完整步骤即可标注“实机通过”，不再把缺少独立实体手机列为阻塞。补充决定：若业务实机操作因双角色、专用资源、地图钓点或活动开放等外部条件卡住，已有设备入口/窗口证据时，允许以“真实协议类型 → 请求门控 → 权威响应 → 状态 → FairyGUI 实际读取的 UI 投影”使用探针冒烟通过作为该项退出依据；这类结果标注为“探针通过”，不冒充完整双角色/资源场景实机操作。

| 功能 | 客户端请求 | 服务端响应 | Android 处理 | UI 绑定 | 真机 | 优先级 |
|---|---|---|---|---|---|---|
| 商城（GameShop） | 有 | 有 | 已实现 | 已实现；135 商品、复古 15 固定格、9 页 | **实机通过**：逍遥完成商品渲染、双币选择和单次购买；点券 `100000→70000`、邮件 `1→2` | ANDROID-01 |
| 师徒（Mentor/Mentee） | 有 | 有 | 已实现 | 已实现 | **探针通过**：逍遥入口 + `MentorRequest → MentorReply → MentorUpdate → UI`，专项 11/11 | ANDROID-02 |
| 关系（Marriage 系） | 有 | 有 | 已实现 | 已实现 | **探针通过**：逍遥入口 + `MarriageRequest → MarriageReply → LoverUpdate → UI`，专项 14/14 | ANDROID-03 |
| 坐骑（Mount） | 有 | 有 | 已实现 | 已实现 | **探针通过**：逍遥专用 fallback 且不误命中排名组件；`@ride → MountUpdate → UI`，专项 8/8 | ANDROID-04 |
| 物品封印/租赁 | 有 | 有 | 已实现 | 已实现 | **探针通过**：逍遥入口 + 封印/租赁双链路请求门控、权威响应与 UI，专项 31/31 | ANDROID-05 |
| 钓鱼 | 有 | 有 | 已实现 | 已实现 | **探针通过**：逍遥入口 + `FishingCast → FishingUpdate → UI`，专项 12/12 | ANDROID-06 |
| 活动/赏金 | 有 | 有 | 已实现 | 已实现 | **探针通过**：逍遥 fallback/切换/分页 + `AcceptQuest → ChangeQuest → UI`，专项 16/16 | ANDROID-07 |

> 注：Hero/ManageHeroes 已另行补齐；ANDROID-02 的师徒验收仅指 Mentor/Mentee，不能与英雄管理混称。
>
> **2026-08-08 代码证据**：ANDROID-01..07 分别对应提交 `db45a7e`/`40fe052`/`1b6eab3`/`cc9ab27`/`4331d10`/`79bb4d8`/`0f2a933`，均含移动端状态/UI 接线与相关自动化测试。这些证据只支持“代码闭环已实现”，不支持“真机门禁已通过”。

**门禁**：上述目标功能**无未知包**（`ServerPacketIds` 差集 = 275 − 246 = 29，逐个确认归属），按设备实操完成请求→响应→状态→UI，或在外部条件卡住时按本节第 41 行的替代使用探针闭环。**不以"所有枚举都有 case"为门禁。**

---

## 3. 构建与 CI 矩阵（替代"整个 sln 全绿"门禁）

| 环境 | 必须构建 | 说明 |
|---|---|---|
| Windows | Shared、Server、Server.MirForms、Client_VorticeDX11、工具项目 | 用 `.slnf` 或明确项目列表 |
| Android runner | Client_MonoGame.Android Release（arm64）、AOT 发布包 | 真实 Android 发布物；BASE-06 的当前四态门禁先以 x86_64 模拟器执行，arm64 真机验收延期至 RELEASE-03 |
| macOS | iOS 工程（仅未来非阻塞验证） | 不进入本轮硬门禁 |
| 通用测试 | 协议、持久化、密码迁移、配置、业务单元测试 | 见 §4.1 测试集 |

> 不使用 `dotnet build Legend of Mir.sln` 作为统一门禁（25 工程混 Windows/Android/iOS 三工作负载，单机不可全绿）。

---

## 4. 阶段任务与依赖

> **门禁之间严格串行，同一门禁内可并行**。任何下一阶段入口任务必须依赖上一阶段 GATE（见 §4.2 门禁 DAG），不可绕过；同一阶段中依赖已满足、文件所有权不冲突的任务，可在独立分支/工作树中由多个会话并行。任务编号全局唯一（前缀：BASE / ANDROID / SEC / DB / PERF / PROTO / RELEASE / EVENT / OPS / OPS-BASIC）。
>
> **每阶段退出条件同时包含"防走偏检查"**：本阶段产出的用户可见工件（代码/diff/截图/通过的测试）≥ 过程资产（分析/计划/矩阵/审核）；若过程资产连续占主导，视为打转信号，按《执行纪律与防走偏铁律》停止条件处理。

### 4.1 最小测试集（§4.1，被 BASE-05/CI 引用）

> v1.4 补齐：此前 CI 矩阵与 BASE-05 引用不存在的 §4.1，已修复。

| 编号 | 测试 | 归属阶段 | 用途 |
|---|---|---|---|
| T-01 | 协议 Golden Vector / 序列化 round-trip（PC/移动/Server 三端一致） | P0 → 增量 P5 | 防协议漂移 |
| T-02 | V1/V2 包兼容测试 | P2 | 传输安全切换不破坏旧客户端 |
| T-03 | 旧密码验证 + Argon2id 透明升级 | P2 | 密码迁移安全 |
| T-04 | SQLite 账户/角色/背包/仓库/邮件 round-trip | P0 → 增量 P3 | 存盘安全 |
| T-05 | 配置文件加载保存，**密码不得落盘** | P2 | 凭据治理 |
| T-06 | 服务端最小资源启动/关闭冒烟 | P0 | 防启动回归 |
| T-07 | 更新清单签名、篡改、防降级测试 | P5 | 发布安全 |
| T-08 | 备份/恢复 + integrity_check | P3 | 数据可恢复 |
| T-09 | 保存代次/单写者并发测试 | P3 | 后台保存正确性 |
| T-10 | RPO 配置强校验（1~5 分钟）与故障注入 | P3 | RPO 保证 |

### 4.2 门禁 DAG（阶段硬依赖）

```
GATE-P0 ──► GATE-P1 ──► GATE-P2 ──► GATE-P3 ──► GATE-P4 ──► GATE-P5（Android 上线）
   ▲           ▲           ▲           ▲           ▲           ▲
 BASE-08    ANDROID-07  SEC-06      DB-05       PERF-03    RELEASE-03
（P0 全绿） （闭环全绿） （安全全绿）  （备份通过）  （回归全绿） （发布闭环全绿）
```

- 任何 `P{N+1}` 阶段任务必须 `addBlockedBy(GATE-P{N})`。
- 任何 `GATE-P{N}` 未通过，`P{N+1}` 不得开始。
- 例外：OPS-BASIC-01..04 是发布前能力，属于 P5 门禁依赖（RELEASE-02/03 依赖它们），不属于后置。

### 4.3 当前开发快照（2026-08-10）

| 门禁 | 实际状态 | 下一个可关闭条件 |
|---|---|---|
| GATE-P0 | **已完成**；远程 CI 证据见 §P0 | 无 |
| GATE-P1 | **已完成**：ANDROID-01 商城逍遥端到端通过；ANDROID-02..07 按本轮替代口径完成真实协议/门控/权威响应/UI 投影使用探针并复核通过；PROTO-01、BASE-10、PERF-00 证据已归档；Base05 集成 223/223 通过；远程 CI `31313826844` 的 Windows、通用测试、Android Release arm64 AOT 全绿 | 无 |
| GATE-P2 | **已完成**：SEC-01～SEC-06 全部完成；签名私钥与发布流水线按边界留在 RELEASE-01/02 | 按门禁顺序进入 GATE-P3 |
| GATE-P3 | **已完成**：DB-01～06 全部完成；备份、恢复、RPO/RTO、配置门禁和真实强停故障注入均有证据 | 进入 GATE-P4，以 PERF-00 基线驱动性能任务 |
| GATE-P4 | **已完成**：PERF-03 深模块与回归完成；300 连接/100 登录协议会话模拟压测通过，PERF-01/02 未发现需猜测性改动的阻断热点；PERF-04 真机渲染基线留后续实测 | 无 |
| GATE-P5 | **已完成**：OPS-BASIC-01～04、PROTO-02/03、RELEASE-01～03 全部完成 | Android 正式上线门禁关闭 |

> SEC-01/02 是 GATE-P1 关闭前已经产生的超前成果，保留代码和证据，但不将其解读为门禁顺序已改变。从本版开始，先关闭 GATE-P1，再合并新的 P2 功能成果。

### 4.4 多会话并行与合并规则

> 本节保留 P0～P5 执行期间的协作规则和任务快照，属于历史记录。当前活动工程治理任务以《工程治理实施路线》为唯一状态源；下表中的“进行中”“待审核”等原始状态不再表示当前仍被占用。

1. 每个会话只领取一个有独立退出条件的任务，明确负责的文件/模块；禁止多会话同时修改同一核心文件。
2. 任务分支从同一个已验证基线创建，会话内自行实现、测试、分阶段提交；不直接在共享 `main` 工作区并行写入。
3. 每个任务交付“提交 + 测试输出 + 门禁证据”；只有达到本任务退出条件才能进入集成队列。
4. 由单一集成会话按依赖顺序合并；每次合并后跑局部回归，一批任务合并后跑完整测试/CI，失败时只回退引入问题的任务提交。任务达到退出条件且集成验证通过后，集成会话自主合并并推送远程，不再逐次要求人工确认；仅破坏性操作、门禁定义冲突或不可恢复风险需升级。
5. 下一门禁的分支不得提前合并。高冲突区（`Shared/Packet.cs`、主循环、网络发送队列、数据库 Schema）默认单会话独占。
6. 任务状态统一使用 `待领取 → 进行中 → 待审核 → 需修正/审核通过 → 已合并`。领取前必须检查 `git worktree list` 与 `git branch --list "codex/*"`；领取后立即创建独立分支/工作树，并在任务自有证据目录提交 `CLAIM.md`（任务、会话、分支、工作树、基线、状态），该提交即为跨会话占用通知。其他会话发现状态为“进行中/待审核/需修正”时必须改领其他任务，不得重复开工。
7. Worker 只修改任务所有权内文件；Reviewer 必须使用独立只读子代理，不得修改文件、构建、暂存、提交、合并或推送。只有单一集成会话可以更新下表的汇总状态并合并到 `main`，避免多个执行会话争写本文。

**当前可分派的独立任务**：

| 任务 | 当前状态（2026-08-09） | 文件/证据所有权 | 退出条件 | 并行关系 |
|---|---|---|---|---|
| P1-VERIFY-A | 旧预验收已审核并合并：`70d1787`；后续设备闭环由 P1-RUNTIME 接管 | `Docs/Evidence/GATE-P1/android-01-03/`；验收时不改生产代码 | 商城/师徒/关系的截图、日志、APK/设备信息归档 | 已停止独立领取，避免与 P1-RUNTIME 重复 |
| P1-VERIFY-B | 旧预验收已审核并合并：`e417e0a`；后续设备闭环由 P1-RUNTIME 接管 | `Docs/Evidence/GATE-P1/android-04-07/`；验收时不改生产代码 | 坐骑/封印租赁/钓鱼/活动的截图、日志、APK/设备信息归档 | 已停止独立领取，避免与 P1-RUNTIME 重复 |
| P1-EVIDENCE | 已审核并合并：`ff0b436` | `Docs/Evidence/GATE-P1/proto-base-perf/`；只读复核现有清单/测试入口 | 清单可读、兼容/资源/性能专项绿，证据归档 | 可与两组真机验收并行 |
| P1-RUNTIME | 已审核并合并：`2c5ecc5`；首轮双币默认扣点券风险已修正，复审无阻塞项 | `src/Clients/Client_MonoGame.Shared/UI/FairyGui/`、Bootstrap 默认配置、`Docs/Evidence/GATE-P1/runtime-20260809/` | 本地服务、资源、账号、APK、设备证据归档；发现的真实缺口转为后续修正 | 已释放文件所有权；后续按坐骑、活动和商城闭环拆分新任务领取 |
| P1-MOUNT | 已审核并合并：`8d0d2f1` | 坐骑窗口解析与 `Docs/Evidence/GATE-P1/mount-window-20260809/` | 不再误命中排名组件；专用 fallback 证据与专项 13/13 | 已释放文件所有权；完整业务闭环另行领取 |
| P1-ACTIVITY | 已审核并合并：`79783e8` | 活动窗口、上下文分页与 `Docs/Evidence/GATE-P1/activity-window-20260809/` | 绕过损坏 FUI；缓存双向切换、分页证据与专项 15/15 | 已释放文件所有权；完整业务闭环另行领取 |
| P1-SHOP | 已审核并合并：`6f91c42` | 商城局部、购买策略与 `Docs/Evidence/GATE-P1/shop-e2e-20260809/` | 135 商品/15 格/9 页，重复点击单请求，数据库购买前后可复核，专项 24/24 | 已完成商城端到端闭环并释放文件所有权 |
| P1-INTEGRATE | 进行中：P1 功能与探针成果已合并，Base05 `223/223` 通过；仅等待远程 CI | `Docs/Evidence/GATE-P1/integration/` 与本文 §4.3 | P1 成果全部合并，完整测试/远程 CI 绿，§4.3 快照更新 | 功能闭环不再阻塞；等待远程 CI |

GATE-P1 关闭后，P2 的 SEC-03、SEC-04、SEC-05、SEC-06 各为独立任务；只有依赖已满足且文件所有权不冲突的任务才能并行。

### P0 可复现基线 + .NET 10 迁移（阻塞，4~6 周）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| BASE-01 | Git/solution 清理：建立 `.git` 基线；移除 8 个幽灵工程引用 + 4 个缺失 Note；恢复/归档一次性工具；建 `.slnf` | — | 指定提交可复现签出 |
| BASE-02 | **可复现构建（本机）**：`global.json` 锁定到可复现稳定 SDK；定义外部资源包/测试资源/地图/补丁资源的来源、版本、哈希、获取方式（README 声明资源在 QQ 群）与**获取脚本**；**建立 `source → acquired → final` 摘要清单**，记录本机资源安装后状态 | BASE-01 | 从指定提交 + 资源获取脚本，**本机**可复现构建；资源 Validate All 通过；摘要清单存在 |
| BASE-02b | **CI 裸克隆可重建（独立后续项，进 backlog）**：需要约 11GB 外部资源镜像基建（CI 可访问的资源仓库/镜像）+ 干净的 agent 环境；本次提交仅将 Micro 启动工件纳入仓库，不替代该资源镜像；不在本轮 P0 验收范围，不作为 GATE-P0 门禁 | — | 单独立项后：裸 clone + 资源镜像可重建 |
| BASE-03 | **CI 骨架**：Windows 构建 + Android runner + 通用测试三段（§3） | BASE-02 | 三段 CI 骨架可跑 |
| BASE-04 | 日志系统恢复：ILog 门面 + 有界异步队列（Debug/Info 满载丢弃并计数，Error/Fatal 走紧急通道）+ 按大小/日期轮转 + 保留天数 + 过滤 PII | BASE-01 | 崩溃现场有最近 N 分钟全量日志 |
| BASE-05 | **最小测试集建立**（§4.1 T-01/T-04/T-06 先行，其余随阶段增量） | BASE-03 | §4.1 已启用项可跑 |
| BASE-06 | **Android/移动共享 net11→net10 迁移**：TFM 改 net10.0-android/ios、MAUI 包降版、workload 锁定、`SupportedOSPlatformVersion` 21→**24**；Debug/Release/AOT+Trim/Trim-only 模拟器验证 | BASE-02 | x86_64 模拟器四态验证通过；真机 arm64 四态延期至 RELEASE-03 最终设备验收；net10 可构建 |
| BASE-07 | **Server/PC/Shared net8→net10 迁移**：TFM 改 net10.0；明确过渡期限；四态验证 | BASE-02 | net10 可构建；无 net8 残留 |
| BASE-08 | **CI 全绿门禁 = GATE-P0**：BASE-05 测试集 + BASE-06/07 构建全绿 | BASE-05/06/07 | **已完成（GATE-P0）；证据见下方**；iOS 不阻塞 Windows/Android 构建 |
| BASE-09 | **iOS 隔离**：隔离 iOS TFM，确保 Windows/Android restore/build 不被 iOS workload 阻塞；不承诺 iOS 可编译 | BASE-06 | **已实现并本机验证**：Shared 默认 `net10.0;net10.0-android`，显式 `EnableIosTarget=true` 时仅求值 `net10.0;net10.0-ios`；Windows/Android graph 不含 iOS，iOS restore 为非门禁 |

**GATE-P0 退出条件**：指定提交 + 资源脚本**本机**可复现构建；CI 矩阵全绿；net10 迁移完成；日志可查；最小测试集绿。（CI 裸克隆可重建 = BASE-02b，独立后续项，不阻塞 GATE-P0。）**不满足不得进入 P1。**

**GATE-P0 当前状态（2026-08-06）：已完成。** 在提交 [`4436426`](https://github.com/Mufeisi/KuMing-monogame/commit/443642644bc709a6059caaa94d84dc7a2eee15fd) 上，[GitHub Actions run 31081000003](https://github.com/Mufeisi/KuMing-monogame/actions/runs/31081000003) 的 `Windows build (solution filter)`、`General tests (discovered projects)`、`Android Release arm64 AOT publish` 三个 job 全绿，Android arm64 AOT 发布工件已上传。BASE-06 的 x86_64 模拟器 Debug/Release/AOT+Trim/Trim-only 四态仍为已验收；本次 arm64 AOT 发布不等同真机测试，arm64 真机四态仍延期至 RELEASE-03。约 11GB 的 BASE-02b CI 裸 clone 外部资源镜像仍在 backlog；本次提交已将 Micro 启动工件纳入仓库。

**P1 当前入口：集成远程 CI 收口。** ANDROID-01..07 的代码、设备入口/窗口和使用探针工件均已归档，不得重复领取；当前仅等待集成提交的远程 CI。

### P1 Android 真机闭环 + 协议盘点（5~6 周）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| ANDROID-01..07 | 功能闭环矩阵实现（商城→师徒→关系→坐骑→封印/租赁→钓鱼/活动） | GATE-P0 | **本地退出项已完成**：ANDROID-01 商城逍遥端到端通过；ANDROID-02..07 使用探针通过并有设备入口/窗口证据 |
| PROTO-01 | **协议盘点**：生成 wire manifest（包 ID/字段顺序/字段类型/枚举底层类型/枚举数值/nullable/数组编码/V1V2 范围）；审计 `LevelEffects` 等已知分叉全部使用点；建立序列化兼容测试 | GATE-P0 | **已完成**：`Docs/protocol-wire-manifest.json` + `ProtocolGoldenTests`（`3e96959`） |
| BASE-10 | 资源来源固定：外部资源包正式纳入版本/哈希清单 | GATE-P0 | **已完成**：版本/哈希契约与获取校验脚本（`1bcc13c`） |
| PERF-00 | 性能测量基建：加 CPU/Update/Draw/DrawCall/纹理切换/创建/GC/显存/保存耗时/网络队列统计 | GATE-P0 | **已完成**；300/100 协议模拟并发已回补，见 `PERF-00性能指标与基线记录.md` |

**GATE-P1 退出条件**：Android 目标功能按 §2 的设备实操或替代使用探针闭环；wire manifest 就绪；集成测试与远程 CI 通过。PERF-00 的会话采集基建属于本门禁代码交付。2026-08-10 后续产品口径明确：开发阶段使用隔离账号和数据库执行模拟协议并发，真机体验由用户后续验证；两类证据必须分开陈述，不得用模拟连接冒充真机角色或设备结果。

**GATE-P1 当前状态：已关闭。远程 CI 证据：[BASE-03 CI 31313826844](https://github.com/Mufeisi/KuMing-monogame/actions/runs/31313826844)。开发队列按顺序进入 GATE-P2。**

### P2 安全改造（4~6 周）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| SEC-01 | **密码与凭据**：服务端新密码 Argon2id（PHC 格式，明确参数/盐长度/回退期限）；老账号成功登录透明升级哈希；**两端正式版禁止持久化密码**（PC 移除 INI 明文读写；移动端默认不保存；后续用可撤销令牌替代，Windows Credential Manager / Android Keystore） | GATE-P1 | 老账号无缝升级；新库无弱哈希；无密码落盘（T-03/T-05 通过） |
| SEC-02 | **传输安全**：新增 TLS 协议 V2（独立端口）；**公网默认关闭 V1 明文登录**（或 V1 仅内网/白名单/安全隧道）；明确最低 TLS 版本、证书域名验证、证书更新策略、V1 停用日期、客户端时间/证书过期处理 | GATE-P1/SEC-01 | V2 全量走 TLS；V1 明文不暴露公网（T-02 通过） |
| SEC-03 | **登录防护**：登录限流 + 失败退避 + IP/账号双维度封禁 | SEC-01 | 压测/脚本可触发封禁 |
| SEC-04 | **管理端安全**：独立管理员凭据 + 角色 + 审计；管理 API 默认只监听回环/内网；不复用游戏 GM 密码 | SEC-02 | 管理端点仅独立凭据可访问 |
| SEC-05 | **配置秘密治理**：生产密钥仅受保护密钥存储（CI 短暂获取，不常驻环境变量）；启动强校验禁止默认 GM 密码/公网 HTTP 管理端点 | SEC-02 | 正式服无法以弱配置启动 |
| SEC-06 | **微端签名格式确定**：ECDSA 签名格式（canonical JSON 或二进制）、Key ID、密钥轮换、单调版本防降级、最低兼容版本（密钥实现与流水线在 RELEASE） | GATE-P1/BASE-10 | 签名格式文档化 + 校验实现（T-07 前置） |

**SEC-01 实施记录（2026-08-08）**：服务端 `AccountInfo.Password` 与注册/改密入口统一写入 Argon2id PHC（`v=19,m=32768 KiB,t=3,p=1`，盐 16 字节，输出 32 字节）；实现使用固定版本 `Konscious.Security.Cryptography.Argon2` 1.3.1（MIT，支持 net10），PHC 编解码和固定时比较在服务端边界完成。新写入使用无填充标准 Base64（`A-Z/a-z/0-9/+`），对已发布旧实现的 `.` 代替 `+` 仅作兼容读取；解析在派生前限制 PHC 总长 256 字符、参数字段 32 字符、内存 16~65536 KiB、迭代 1~4、并行度 1~4、盐/摘要 16~64 字节，超限或畸形输入直接拒绝。固定密码/盐的标准 PHC 互操作向量与旧点号兼容向量见 `PasswordSecurityTests`。旧 PBKDF2-SHA1（50 次、现有 24 字节盐）仅在成功认证时兼容一次并立即重哈希；认证经 `Envir.VerifyAccountPassword` 接缝在主线程更新账户并触发 `RequestAutoSave`，SQL 保存后重载验证见 `SqlPersistenceRoundTripTests`。错误密码和畸形 PHC 不升级；兼容回退期限暂定为 2026-12-31，届时移除旧格式验证路径（若迁移未完成需另立安全变更）。PC 与移动正式版加载时清理旧密码字段，保存始终写空值；移动端 `RememberPassword` 固定为 false，不再作为保存条件。SmokeTest 若需要密码，只接受显式环境变量 `LYOCRYSTAL_SMOKETEST_PASSWORD` 或本次进程内存注入，不写配置。T-03/T-04/T-05 专项证据见 `Tests/Base05.Tests`，SEC-02/TLS、Credential Manager/Keystore 仍按后续任务处理。

**SEC-01 P1 收口记录（2026-08-08）**：旧 PBKDF2 摘要的历史实现是 24 字节结果直接 `Encoding.UTF8.GetString`；兼容验证在 PBKDF2 派生和 `Encoding.GetBytes` 前限制不超过 24 个 UTF-16 单元并拒绝未配对代理项，转换仍在受控异常边界内，超长/非法输入不升级。PC 与移动 Settings 在构造 `InIReader` 前通过 `Shared.Security.SensitiveIniSanitizer` 读取快照，遍历并删除 `Game`/`Launcher` 节内全部重复 `Password`、`RememberPassword` 键后以同目录临时文件原子替换；读取、写入或替换失败即抛出并阻止空配置继续启动。PC 补丁源在 `NeedLogin=true` 时只从运行时环境读取 `LYOCRYSTAL_PATCH_PASSWORD`（用户名可由 `LYOCRYSTAL_PATCH_USER` 覆盖配置），凭据仅在内存组装 Basic Auth；缺少凭据时记录无秘密错误并提示，清空下载队列，不发送 `用户名:`。P1 专项证据见 `PasswordSecurityTests`、`PasswordStoragePolicyTests`。

**SEC-02 阶段A实施记录（2026-08-08）**：服务端新增独立 TLS V2 端口与 `SslStream` 传输接缝，最低 TLS 1.2；证书路径来自正式配置，证书密码只从运行时环境变量 `LYOCRYSTAL_TLS_CERT_PASSWORD` 读取；非回环地址默认关闭 V1 明文监听，仅回环或显式开发开关允许。T-02 阶段A已用临时自签证书完成严格信任握手、现有 Packet 往返、过期/不受信任证书拒绝、端口冲突与双监听停止释放验证。PC/Mono/Android 客户端 TLS 接线、客户端域名/链校验和 ConfigForm 配置界面留待阶段B，当前不宣称完成。

**SEC-02 阶段B1实施记录（2026-08-08）**：PC 客户端复用同一 Packet 解析/发送队列和 Stream 接缝，V1 使用 `NetworkStream`、V2 使用 `SslStream` 独立连接 `TlsPort`；`TlsClientPolicy` 强制非空 `TlsServerName`、TLS 1.2/1.3，默认系统信任链、域名、有效期严格校验，无回调绕过、客户端证书或密码落盘。PC `UseTlsV2=false` 时仅允许回环开发地址，非回环直接拒绝且 TLS 失败不降级 V1。T-02 B1 临时证书真实握手/Packet 往返、错误主机、不受信任/过期证书及非回环门禁专项已通过；Mono 客户端 TLS 接线（B2）、ConfigForm UI 与证书固定仍未完成。

**SEC-02 阶段B2实施记录（2026-08-08）**：Mono/Android 主 Network 复用同一 Packet 解析/发送队列和 `Stream` 接缝，V1 使用 `NetworkStream`、V2 使用 `SslStream` 独立连接 `TlsPort`；`UseTlsV2`、`TlsPort`、`TlsServerName` 已接入移动 Settings 的 Load/Save。非回环地址关闭 V1 明文，TLS 失败不降级，目标主机为空由共享 `TlsClientPolicy` 拒绝；日志仅记录异常类型与目标主机/端口，不记录证书或秘密。沿用 T-02 TLS 专项的严格域名/链/过期验证与 Packet 往返证据，TLS 专项 11/11、Base05 全量 181/181、Mono net10 Release 与 Android Release 构建均通过；ConfigForm UI、证书固定、生命周期及 iOS 仍未完成。

**SEC-02 C1 服务端硬化记录（2026-08-08）**：V1 明文仅允许回环或显式开启的 RFC1918 IPv4、IPv6 链路本地/ULA 地址，公网、`0.0.0.0` 与 `IPv6Any` 始终拒绝；TLS 接收先登记下一次 accept，再以可取消的 10 秒异步握手处理，失败只记录异常类型并关闭连接。启动时至少一个游戏监听器成功才进入 Ready，TLS-only 纳入 `IsNetworkBound`；证书/端口/监听失败会清理 listener、证书与线程状态并将 `Running=false`，支持修正配置后重试。C1 TLS 专项 16/16；ConfigForm、证书固定、客户端生命周期与 iOS 仍未完成。

**SEC-02 C2 单写者收口记录（2026-08-08）**：Shared 新增 `StreamWriteGate` 原子门闩，Server `MirConnection`、PC 与 Mono/Android Network 在从现有发送队列取批次前先占用门闩，忙时保留队列并返回；`EndWrite` 回调在 `finally` 释放，写入失败统一断开，重连时旧门闩与旧流回调不会释放或断开新连接。Server 断开包仅在门闩空闲时尽力发送，忙时走幂等关闭，不与普通写重叠；未复制 Packet/业务队列、未增加后台线程。C2 证据限定为 StreamWriteGate 门闩单测与 PC/Mono/Server 三端静态接线，不能等同完整运行时端到端证明。Android 构建留待后续轮次。

**SEC-02 C3 收口记录（2026-08-08）**：真实服务端验收拆为四个相互独立的生产路径用例：错误 PFX 密码失败后修正重启、TLS KeepAlive 往返、回环 V1 KeepAlive 往返、TLS 端口占用失败后释放重启；每例独立临时 SQLite/端口/证书/密码环境作用域，临时目录清理失败仅记录并保留路径，不阻断测试。客户端 TLS 失败提示补充系统时间、`TlsServerName`/证书 SAN、证书链和有效期排障线索；运维配置见 `Docs/SEC-02-TLS运维配置.md`（PFX、`LYOCRYSTAL_TLS_CERT_PASSWORD`、TLS 1.2、轮换/回滚/到期监控、客户端时钟、公网 V1 禁止及私网 V1 2026-12-31 停止）。C3 专项 4/4、TLS/生命周期合并专项 21/21、Base05 全量 194/194；Server.Library、Server.MirForms、PC、Mono net10 Release 与 Android Release 构建均 0 错误（既有警告不阻断）。以上为 C3 运行证据，不表示 GATE-P2 已关闭。

**SEC-02 当前状态（2026-08-08）**：C4 仅收口 TLS 运行时握手有界、连接准入临界区和 Stop/Restart 代次取消；PC/Mono 代次与异步握手以源代码静态接线和三端构建证据核对，未构造完整客户端宿主。生产增量约 185 行，原因是 PC/Mono 双端各自现有接缝接入同一代次语义，未新增通用抽象。PFX 环境变量仍是 SEC-05 受保护存储接入前过渡，C2 证据已降级为门闩单测与三端静态接线。SEC-02 仍未完成，存在越阶段记录，GATE-P2 未关闭；不得以本记录替代 SEC-03～SEC-06、SEC-05 及公开发布门禁。
**SEC-02 C4.1 收口记录（2026-08-08）**：Server 在停止/重启代次切换时将 TLS 在途计数归零，accept 异常统一释放计数；PC/Mono 连接发布、当前代次断开与接收回调使用现有小锁，回调携带自己的 Stream/代次并只对当前连接断开。C4.1 专项 24/24；本轮生产新增 123 行（仅三端既有接缝，无新通用抽象）。客户端仍以静态接线与构建证据为主，SEC-02/GATE-P2 状态不变。

**SEC-02 C4.2 收口记录（2026-08-08）**：Server 将 TLS generation、取消源与在途计数封装为私有代次状态，Start/Stop 原子替换或取消；accept/handshake 只操作其捕获代次，旧代释放不会改动新代。PC/Mono 新增 `FailIfCurrent(expectedClient, generation)`，连接失败、NotConnected、EOF 与接收异常在锁内核对并摘除当前状态，锁外关闭流/客户端并刷新 trace；普通 Disconnect 同样锁内摘除，旧客户端失败不会摘除新代。客户端完整宿主端到端未构造，代次隔离以源代码静态接线与三端构建证据核对；TLS 专项 17/17、Base05 全量 198/198；PC、Mono net10 与 Android Release 构建 0 错误（Server 未改，沿用 C4.2 既有通过证据）。

**SEC-02 C4.3 收口记录（2026-08-09）**：PC/Mono `Connect` 在 `_connectionGate` 内以局部 `TcpClient` 原子安装连接代次、写门闩与 TLS 状态，锁外仅对捕获 client 调用 `BeginConnect`；已有连接只释放局部 client 并返回。连接失败回调移除无条件重连，交由现有 `Process` 主循环重试；旧代失败不会摘除新代，EOF/Receive 逻辑保持。未构造完整客户端宿主，采用源代码静态接线与 TLS 专项 17/17、Base05 全量 198/198、PC/Mono/Android Release 构建证据；生产代码净增 13 行。SEC-02/GATE-P2 状态不变。

**SEC-02 C5 配置界面收口记录（2026-08-09）**：服务端 `ConfigForm` 的现有网络页已接入 `TlsEnabled`、`TlsPort`、`AllowLegacyV1` 与 `TlsCertificatePath`，证书密码仍只从 `LYOCRYSTAL_TLS_CERT_PASSWORD` 读取，不进入界面或配置文件。启用 TLS 时统一复用 `TlsTransportPolicy.ValidateConfiguration`，端口为零/冲突、证书路径为空、文件不存在、PFX 损坏或证书不合格均阻止保存并停留在网络页；服务启动复用同一策略。策略专项 18/18、真实 Windows STA 表单宿主 1/1、Base05 全量 229/229、Server.MirForms Release 构建通过。证据见 `Docs/Evidence/GATE-P2/sec02-c5-configform-20260809/`；证书固定、完整客户端宿主及 SEC-03～06 仍未完成，SEC-02/GATE-P2 状态不变。

**SEC-02 C6 客户端宿主收口记录（2026-08-09）**：PC Windows 宿主直接调用正式客户端 `Network.Connect`，验证不受信证书拒绝后连接状态清空且不降级 V1。Android 增加显式 `sec02TlsHostProbe` Intent，只在内存中临时覆盖 host/port/serverName，12 秒内验证正式 `Settings → Network → SslStream` 握手后恢复原设置，不发送账号或游戏包；失败输出有界分类和异常类型链，不记录秘密，迟到旧代回调不能覆盖超时分类。逍遥实机以旧端点复现 `FAIL:网络端点;SocketException:Connection refused`，黑洞/抖动端点稳定分类为“握手超时”，随后对受信 `www.cloudflare.com:443` 返回 `SEC02_TLS_HOST_PROBE:PASS`，排除 Android 证书链及在线吊销兼容性问题。TLS 专项 19/19、PC 宿主 1/1、Base05 全量 230/230、Android Release arm64 AOT 构建通过；证据见 `Docs/Evidence/GATE-P2/sec02-c6-client-host-20260809/`。证书固定及 SEC-03～06 仍未完成，SEC-02/GATE-P2 状态不变。

**SEC-01 P2 收口记录（2026-08-09）**：`HTTPLogin` 的完整账户事务统一投递到服务端主线程；排队超时使用原子取消，已开始执行则等待真实结果，停服后禁止回退到调用线程。认证或提交异常会恢复密码哈希/盐、封禁状态、错误次数与自动保存请求。PC 与移动端登录统一通过实际 `Settings.Load/Save` 接缝，PC 集成测试从真实 `Network` 发送队列取回同一登录包；Android 使用显式 Intent 触发的有界宿主探针，真实执行 `Settings.Save → Network.Enqueue`，验证队列增加且配置无密码后恢复原账号状态。专项 `5/5`、Windows 宿主 `1/1`、Android Release arm64 AOT 与逍遥日志 `SEC01_HOST_PROBE:PASS` 均通过；证据见 `Docs/Evidence/GATE-P2/sec01-http-login-20260809/`。SEC-01 完成不代表 GATE-P2 或 SEC-02 完成。

**SEC-03 收口记录（2026-08-10）**：新增单一 `LoginProtection` 策略，PC 游戏登录与服务主线程 `HTTPLogin` 统一接入账号/IP 双维度的全请求窗口限流、失败指数退避和临时封禁。默认账号/IP 请求上限分别为 `30/60s`、`120/60s`，失败窗口 `300s`，第 `6/20` 次失败分别封账号/IP `15min`，退避 `500ms` 起、最高 `30s`；成功登录清失败状态但不清当前请求窗口。账号封禁复用可保存的 `Banned/BanReason/ExpiryDate`，IP 封禁复用 `IPBlocks`；跟踪键容量有界，不新增线程、协议或数据库表。真实服务主线程脚本已跨 IP 触发账号封禁、跨账号触发 IP 封禁，高频成功登录限流专项已通过；登录安全与 SEC-01 合并专项 `13/13`，基于已合入 SEC-02 C6 的最新主线复验 Base05 全量 `238/238`。证据见 `Docs/Evidence/GATE-P2/sec03-login-protection-20260809/`。SEC-03 完成不代表 GATE-P2 关闭。

**SEC-02 C7 证书固定收口记录（2026-08-10）**：共享 `TlsClientPolicy` 新增 `sha256/<Base64>` SPKI 固定校验，PC、Mono 与 Android 正式连接从 `[Network] TlsSpkiSha256Pins` 读取；最多支持 4 项，允许当前/下一证书双固定值平滑轮换。固定值是系统信任链、域名、有效期和在线吊销检查之外的附加条件，任何系统证书错误都不能被固定值绕过；空配置保留分阶段发布能力，正式发布要求至少配置当前证书固定值。真实 `SslStream` 正确固定值握手与 Packet 往返、错误值、格式边界和系统证书错误拒绝均已验证；TLS 专项 `20/20`、Base05 全量 `239/239`，PC、Mono 多目标与 Android Release 构建均 0 错误。证据见 `Docs/Evidence/GATE-P2/sec02-c7-cert-pinning-20260810/`。SEC-02 至此完成；SEC-04～06 仍阻塞 GATE-P2。

**SEC-04 管理端安全收口记录（2026-08-10）**：现有 `HttpServer` 的管理端点新增独立 Bearer 凭据，不再以游戏 `GMPassword` 或仅来源 IP 作为授权。`Administrator` 可访问状态、广播、开户和名单维护，`Operator` 仅可访问状态与广播；比较使用固定长度 SHA-256 摘要和固定时间比较，同值角色令牌按未配置失败关闭。回环可使用 HTTP，明确内网 IP 必须使用 HTTPS，公网 IP、通配地址、主机名和内网明文 HTTP 在启动前拒绝；`HTTPTrustedIPAddress` 继续限制来源。GET 与非 GET 管理请求统一经过来源、鉴权和审计；每次拒绝、失败、越权和成功均通过不可静默丢弃的警告级日志接缝写入 `ADMIN_AUDIT`，来源以确定性散列关联标识记录，且不记录 IP 明文、Authorization、令牌或查询串。专项 `4/4` 含真实 `HttpListener` 的 401/403/405/成功路径和真实日志落盘；证据见 `Docs/Evidence/GATE-P2/sec04-admin-security-20260810/`。环境变量令牌是 SEC-05 受保护密钥存储接入前过渡；SEC-05、SEC-06 仍阻塞 GATE-P2。

**SEC-05 配置秘密治理收口记录（2026-08-10）**：服务端新增 Windows DPAPI `CurrentUser` 受保护秘密存储，TLS 证书密码、管理端角色令牌、游戏 GM 密码、MySQL 连接串、微端 Code 与 AI API Key 均不再从 INI 或常驻环境变量读取；历史明文键在 `Settings.Load` 时严格删除。CI/运维只允许通过专用 `LYOCRYSTAL_IMPORT_*` 变量短暂导入，进程读取后立即清空；旧 TLS、管理端和 AI 环境变量按迁移错误失败关闭。正式启动在脚本、监听器和服务线程之前统一校验：默认或短 GM 密码、缺失的已启用功能秘密、同值或过短管理令牌、公网/通配管理监听及内网明文 HTTP 均阻止启动。DPAPI 往返、文件无明文、短暂导入清理、旧 INI 删除、完整配置覆盖和启动前失败专项与关联安全/生命周期测试 `38/38`，Base05 全量 `249/249`；Server.Library 与 Server.MirForms Release 构建 0 错误。运维与恢复说明见 `Docs/SEC-05-受保护秘密与启动门禁.md`，证据见 `Docs/Evidence/GATE-P2/sec05-secret-governance-20260810/`。SEC-05 至此完成；SEC-06 仍阻塞 GATE-P2。

**SEC-06 微端资源索引签名收口记录（2026-08-10）**：PC 与 Mono/Android 的现有远端 Bootstrap 更新接缝统一复用 `BootstrapManifestSignaturePolicy`，仅在严格 JSON 包装通过确定性二进制载荷、ECDSA P-256/SHA-256/P1363、编译期 SPKI Key ID 信任表、密钥序列窗口、宿主显式最低客户端版本和持久化单调序列校验后生成更新队列。低序列、同序列异资源版本或异载荷、未知/过期密钥、哈希或签名篡改、未知字段、重复包、非规范摘要、损坏状态、当前安装中单独删除状态及替换为旧有效状态快照均失败关闭；安装标记绑定最高序列与载荷摘要，状态先落盘的崩溃窗口可在验签后安全前推标记。保存的原始清单在重启后重新验签，更新队列的资源版本、包名和 SHA-256 逐项匹配该清单；PC 与 Mono/Android 正式下载不可关闭地使用签名摘要校验 ZIP，旧队列、篡改队列或 ZIP 不再继续。签名载荷的字节级顺序、字段边界、轮换步骤与 RELEASE-01 接缝见 `Docs/SEC-06-微端资源索引签名格式.md`。SEC-06 不生成或托管私钥，生产公钥注入、CI 签名和事务发布仍按 PRD 留在 RELEASE-01/02；在生产公钥注入前，远端更新按设计拒绝。专项 `8/8`、Base05 全量 `257/257`，PC、Mono net10 与 Android arm64 Release/AOT 构建均 0 错误；证据见 `Docs/Evidence/GATE-P2/sec06-micro-signing-20260810/`。SEC-01～SEC-06 至此全部完成，GATE-P2 关闭，开发队列进入 GATE-P3。

**DB-01 SQLite 单写线程收口记录（2026-08-10）**：SQLite 正式连接启用 WAL、私有连接缓存与 5 秒 `busy_timeout`；运行期世界、关系、账户、行会、商品、攻城和角色归档保存先在调用主线程捕获既有快照，再由唯一后台写线程串行提交。同一数据域尚未开始的请求合并为最新快照，已开始事务不被替换。关服先在主线程停止游戏监听并完成玩家断开，让宠物、关系、行会在线状态和最后登出时间等变化进入内存，再捕获账户、行会、商品与攻城最终快照并排空；连续保存失败达到既有阈值时仅重绑监听而不重载数据库，恢复后可重试。通过失败检查后当前帧立即退出、剩余主线程工作被取消且重启不执行，资源清理不再追加商品保存。真实 WAL 写事务期间 8 路并发读、同域 100 次合并、单写并行度、排空等待与关服失败策略均已验证；专项 `12/12`、Base05 全量 `265/265`，Server.Library 与 Server.MirForms Release 构建 0 错误。实现说明见 `Docs/DB-01-SQLite单写线程与关服策略.md`，证据见 `Docs/Evidence/GATE-P3/db01-sqlite-writer-20260810/`。DB-01 完成不代表 GATE-P3 关闭；保存代次、`synchronous=FULL` 与完整后台保存口径仍属于 DB-02。

**DB-02 后台保存语义收口记录（2026-08-10）**：SQLite 保存为每次快照分配进程内单调代次，快照工厂在调用线程创建与游戏可变状态脱离的 DTO，再由不暴露载荷的独占所有权信封交给唯一写线程。写线程按数据域拒绝迟到旧代，只有成功事务推进最后成功提交代次；完整域快照仍可用更高代次合并尚未开始的同域请求，逐角色增量归档则逐项排队、不参与合并。正式连接显式启用 `synchronous=FULL`，`SaveSnapshotCapture` 与 `SaveTransactionCommit` 分别度量调用线程捕获和后台事务提交。账户测试验证交接后修改在线对象不会改变已交接快照，捕获与提交位于不同线程；专项与全量计数、两项 Release 构建证据见 `Docs/Evidence/GATE-P3/db02-save-generation-20260810/`。实现说明见 `Docs/DB-02-后台保存快照与代次.md`。DB-02 完成不代表 GATE-P3 关闭；在线备份与恢复仍从 DB-03 继续。

**DB-03 SQLite 在线备份收口记录（2026-08-10）**：正式宿主在 SQLite 资源加载后启动独立备份服务，启动即后台首备并按配置周期执行；使用 Microsoft.Data.Sqlite 在线 Backup API 从 WAL 源库生成一致副本，本地和异地副本均经 `integrity_check=ok` 后从 `.partial` 原子发布。保留策略只清理 `lyocrystal-sqlite-*.db` 受管文件；根目录、文件占位、相同或嵌套目录被拒绝。正式服 SQLite 不允许关闭自动备份，异地目录只接受 UNC 或不同卷，且本地状态目录和异地目录必须在进入 Ready 前真实可写。Administrator 可经受鉴权、审计的 `POST /backup/run` 一键触发，Operator 可查询 `GET /backup/status`；最近状态同时原子持久化，运行中断或状态损坏在重启后转为失败。本地副本发布后先记录有效路径再执行保留，清理失败不会隐藏已生成副本。WAL 未提交事务隔离、本地/异地可读性、当前 Windows `C:`→`D:` 真实跨卷复制、损坏拒绝、自动首备、保留、状态恢复与真实端点角色边界均有专项验证；最终计数与构建证据见 `Docs/Evidence/GATE-P3/db03-online-backup-20260810/`，运维说明见 `Docs/DB-03-SQLite在线备份与状态监控.md`。DB-03 不替代 DB-04 的空环境/强停恢复演练，GATE-P3 仍未关闭。

**DB-06 MySQL 切换门槛收口记录（2026-08-10）**：SQLite 保持默认数据库；只有峰值在线玩家连续 7 天达到 500、主库连续 3 天达到 10 GiB、`SaveTransactionCommit` P95 连续 3 天达到 750ms，或保存失败连续 3 小时达到每小时 3 次中的任一项，才进入 MySQL 迁移规划。单次尖峰、窗口不足或没有触发项均明确返回继续 SQLite。迁移前门禁直接重新判定原始指标，要求备份服务源库与当前 `Settings.SqlitePath` 一致，以唯一触发标识复用 DB-03 服务现场生成新本地/异地副本，强制异地为 UNC/不同卷，再次检查两份数据库完整性，并将格式版本、规范化源路径、指标、路径和 SHA-256 作为 Windows DPAPI `CurrentUser` 受保护授权原子保存。正式 `CreateFromSettings()` 选择 MySQL 前必须解密记录、核对当前源库、重新计算门槛并复验完整性与摘要；仅修改 provider、手写 JSON、使用其他源库/同卷目录或篡改副本均失败关闭。DB-06 不实现实际迁移、双写、Schema 映射或回切；说明见 `Docs/DB-06-MySQL切换门槛与迁移前备份.md`，证据见 `Docs/Evidence/GATE-P3/db06-mysql-threshold-20260810/`。DB-05 仍未完成，GATE-P3 保持开启。

**DB-04 SQLite 恢复演练收口记录（2026-08-10）**：现有 `Server.MirForms` 宿主提供离线 `--restore-sqlite` 模式，复用 Server 库恢复接缝；来源必须是 DB-03 独立主库副本，来源或目标目录 `.partial` 任一 `integrity_check` 失败都不会覆盖原库。强停目标先在离线独占条件下 checkpoint 已提交 WAL、切换 DELETE journal，并复制刷新为已验证单文件 `.pre-restore` 回滚工件，再以同目录 `File.Replace` 原子发布；任一进程中断点上正式目标都是独立可读的旧库或新库。发布后校验失败会原子回滚，回滚步骤失败则汇总报告不完整。当前 Windows Release 真实演练使用 DB-03 正式服务生成 C: 本地和 D: 异卷同哈希副本；备份后提交账号金币 777，在 WAL/SHM 存在时强停 PID 1156，正式 CLI 返回 0，恢复后五个业务域复读成功且宿主进入 `Ready`。可复算 RPO 为 303ms；恢复命令到 Ready 的 UTC 端点差为 602.31ms，保守上取整按 RTO 603ms 报告（进程内 Stopwatch 为 600ms）；故障到 Ready 为 625ms，满足 RPO≤5 分钟、RTO≤30 分钟。原始命令、时间、PID、文件/hash、stdout/stderr 和退出码见 `Docs/Evidence/GATE-P3/db04-restore-drill-20260810/raw-powershell-transcript.txt`。DB-04 不替代 DB-05 的生产保存间隔及最坏崩溃点故障注入，GATE-P3 仍未关闭。

**DB-05 RPO 配置强校验收口记录（2026-08-10）**：`ProductionRpoPolicy` 统一约束自动保存间隔，所有环境拒绝零和负数，正式环境只接受 1～5 分钟；`Settings.Load`、`ConfigForm.TrySave` 和 `ProductionSecurityPolicy` 分别在配置载入、界面保存和工作线程创建前失败关闭，并按同一 `TestServer` 状态决定是否放宽上限。Envir 首次保存截止时间和后续重新排期复用同一截止时间计算，正式默认仍使用现有 Stopwatch，internal 时间接缝只用于确定性测试。T-10 子进程在正式最坏值 5 分钟下启动真实 Envir 与 SQLite 持久化，在 300000ms 调度点等待账户域后台事务产生成功代次；随后把内存金币从 100 改为 777，在下一截止 600000ms 前 1ms 由父测试强停整个进程树，不执行 Stop/最终保存。新持久化实例重载仍读到最后成功提交值 100，可复算未提交窗口 299999ms，小于 5 分钟。PID、非零退出码、提交代次、逻辑时刻和重启值随 TRX 归档；说明与证据见 `Docs/DB-05-RPO配置强校验.md` 和 `Docs/Evidence/GATE-P3/db05-rpo-guard-20260810/`。磁盘、权限或事务失败仍由既有保存韧性策略告警/保护，不能伪称继续满足时间 RPO。DB-01～06 至此全部完成，GATE-P3 关闭，开发队列进入 GATE-P4。

**GATE-P2 退出条件**：公开测试前安全项全部完成；凭据不通过未加密网络传输；公网无 V1 明文登录。

### P3 SQLite 生产化（3~5 周）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| DB-01 | WAL + busy_timeout + **专用单写线程**（同时仅一个保存任务，新请求合并，关服等最后一次提交，保存失败告警/重试/关服策略） | GATE-P2/BASE-04 | 高并发读写无锁死；单写者保证（T-09 通过） |
| DB-02 | 保存语义：**完整后台保存落地**（不可变快照 DTO + 单调递增保存代次；主线程只做快照捕获交接，专用写线程提交；`synchronous=FULL` 默认）；分别度量"主线程快照捕获耗时"与"后台事务提交耗时" | DB-01 | 快照/代次实现 + 测试；两类耗时分别可测 |
| DB-03 | 在线备份（SQLite Backup API 或 `VACUUM INTO`）+ `integrity_check`（在备份副本上执行）+ 自动备份保留策略 + 异地副本 + **备份状态监控** | DB-02 | 一键备份/恢复演练通过（T-08 通过） |
| DB-04 | 恢复演练：空环境恢复 + 强制停止后恢复验证；明确 RPO ≤ 5min、RTO ≤ 30min；每个版本至少一次真实恢复演练 | DB-03 | 演练文档 + 通过记录 |
| DB-05 | **RPO 配置强校验**：生产环境 `SaveDelay` 强制 1~5 分钟（`Settings.cs:529`/`ConfigForm.cs:99`/`Envir.cs:704` 加校验，越界拒绝或告警）；故障注入验收 | DB-02 | 越界配置无法生效；故障注入下 RPO 达标（T-10 通过） |
| DB-06 | MySQL 切换触发门槛定义（在线数/数据量/故障指标）；迁移前强制备份 | DB-02 | 门槛文档化，未触发则维持 SQLite |

**GATE-P3 退出条件**：备份恢复演练通过；RPO/RTO 达成（含配置强校验）；崩溃后最多丢失自上一次成功提交以来的内存状态，最后一次成功提交的库保持一致。

### P4 基于指标的性能优化（4~6 周）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| PERF-01 | 热点优化：**仅优化排名靠前且可复现的瓶颈**（ID/标准化精确名称可索引；模糊/语义查找不强行 O(1)） | GATE-P3/PERF-00 | **已完成（测量关闭）**：300/100 模拟协议并发无协议失败，未发现需猜测性优化的阻断热点 |
| PERF-02 | 网络与 GC：**确认分配热点后**再用 ArrayPool/预分配（否则避免所有权/脏数据风险） | GATE-P3/PERF-00 | **已完成（测量关闭）**：心跳与 GC 指标已采集，未发现需引入所有权风险的阻断热点 |
| PERF-03 | 巨型类处理：**优先提取有边界的深模块 + 回归测试**；`partial` 仅拆文件不承诺降耦合 | GATE-P3/PERF-00 | **已完成**：`EquipmentSetBonusModule` 两方法接口 + 六类回归，`HumanObject` 移除原 267 行规则 |
| PERF-04 | 渲染性能：地图静态层/对象合批作为**候选方案**，由基线数据决定是否实施 | GATE-P3/PERF-00 | **本轮不实施**：无渲染基线，不改现有渲染接缝 |
| PERF-05 | 稳定性验收口径定稿 | GATE-P3/PERF-00 | **已完成（产品决定）**：开发阶段采用模拟协议压测；真机体验与长时 soak 由用户后续验证 |

**GATE-P4 退出条件（2026-08-10 产品决定）**：PERF-03 至少提取一个有真实接口的深模块并具备回归测试；300 个模拟协议连接中 100 个使用隔离测试账号完成登录并持续心跳，掉线后自动补足目标连接数；专项、全量和相关平台构建通过。模拟压测不要求生产账号或几百台真机，也不等同角色战斗、渲染或 24/72h soak；这些实机体验由用户后续验证。

**PERF-03/GATE-P4 收口记录（2026-08-10）**：`HumanObject.RefreshStats` 原有普通套装、圣龙戒指、神龙部位与天龙组合规则已迁入纯进程内 `EquipmentSetBonusModule`；模块以 `ApplyItemSetBonuses`、`ApplyMirSetBonuses` 两个接口隐藏 263 行实现，巨型类移除原 267 行规则，不使用 `partial` 或假适配器。回归覆盖破碎、强青玉、圣龙左右戒、神龙局部/完整、天龙四层累积及原有提前结束语义；专项 6/6、Base05 全量 322/322、Server.Library 与 Server.MirForms Release 均 0 错误，证据见 `Docs/Evidence/GATE-P4/perf03-deep-module-20260810/`。

**PERF-01/02 模拟并发回补记录（2026-08-10）**：新增测试项目内的协议模拟客户端，以独立测试子进程复用正式 `Envir.Main`、TLS/`SslStream`、Packet、登录与 PERF-00 接缝，避免静态状态污染全量测试；每次运行创建临时 SQLite 和 100 个 Argon2id 测试账号，不接生产数据库。阶段运行维持 300 个同时在线连接，其中 100 个完成登录并逐槽持续心跳；主动断开后补足目标连接数，稳定窗结束前再次确认 300/100 在线。30 秒窗口记录峰值 300、登录成功 101 次、心跳响应 23197、补连 1、建连重试 0、协议失败 0、心跳 p95 297.16ms、GC pause p95 39.32ms。未观察到需要猜测性 ArrayPool、协议或渲染改造的阻断热点，PERF-01/02 按测量结果关闭；PERF-04 的真实渲染与设备体验留用户后续实机验证。证据见 `Docs/Evidence/GATE-P4/perf01-02-simulated-load-20260810/`。

### P5 协议 canonicalization + 签名发布流水线 + 发布前能力（4~6 周）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| OPS-BASIC-01 | **已完成**：最小服务端监控 + 告警；在线/Tick p95/保存耗时/队列积压/备份状态 + 告警（发布前基础版） | GATE-P4/DB-03 | Operator 可读真实管理端点；告警触发/恢复有测试与日志接缝 |
| OPS-BASIC-02 | **已完成**：崩溃诊断基础；PC/Android/服务端崩溃收集最后一段日志 + 版本 + 资源版本 + 脱敏配置摘要 | GATE-P4/BASE-04 | 崩溃现场可离线定位 |
| OPS-BASIC-03 | **已完成**：Kill Switch；可远程关闭商城/更新/活动/高风险功能 | GATE-P4/RELEASE-02 | 开关生效 + 审计 |
| OPS-BASIC-04 | **已完成**：依赖漏洞扫描 + SBOM + 许可证审计；外部素材/字体/FairyGUI/音频/地图授权清单 | GATE-P4 | 清单文档化 + 扫描通过 |
| PROTO-02 | **已完成**：Android 正式构建改为直接链接 `Shared` 协议事实源，PC/Server 继续引用同一项目；自动生成 wire manifest 并纳入 CI 漂移审计 | PROTO-01 | 三端同一协议源；兼容矩阵绿；manifest 自动生成无漂移 |
| PROTO-03 | **已完成**：服务端/客户端/协议/资源版本兼容矩阵及最低兼容规则已文档化 | PROTO-02 | 矩阵文档化 |
| RELEASE-01 | **已完成**：签名实现 + 密钥管理；APK 签名密钥与资源清单签名密钥分开；清单签名覆盖所有资源包哈希；Key ID/轮换/防降级；CI 从受保护密钥存储短暂获取 | SEC-06/OPS-BASIC-04 | 签名校验通过（T-07 通过）；密钥不出安全存储 |
| RELEASE-02 | **已完成**：发布流水线一条命令完成构建、冒烟、导出、签名、5% 灰度与回滚；PC/Android 文件事务执行下载后验证、原子切换、失败/崩溃恢复；错误率、崩溃率与连续致命崩溃触发自动回滚 | RELEASE-01/OPS-BASIC-01/02/03 | 一键发布 |
| RELEASE-03 | **已完成**：Android 生命周期与设备验收；arm64 Debug/Release/AOT+Trim/Trim-only 四态；安装/覆盖升级/失败回滚；首次签名资源整版下载/恢复/空间故障注入；登录握手与既有业务回归；前后台/锁屏/网络/多分辨率；最低 API 24；AOT/Trim 反射启动；APK 签名密钥备份与灾难恢复 | RELEASE-02/OPS-BASIC-01..04 | 验收清单全绿；实体手机特有体验由项目所有者后续观察 |

**GATE-P5 退出条件（Android 正式上线硬门禁）**：发布闭环全绿 + 生命周期验收通过 + OPS-BASIC-01..04 全部就绪。

**OPS-BASIC-01 收口记录（2026-08-10）**：正式服务端在管理 HTTP 启用时自动启动不导出的 PERF-00 基础监控会话；`BasicOperationsMonitor` 只组合既有 PERF-00 在线数、Tick p95、保存 p95/失败、网络队列 gauge 与 DB-03 备份快照，不跨线程遍历玩家状态。Operator/Administrator 可经 SEC-04 来源限制、Bearer 鉴权和审计读取 `GET /operations/status` JSON。Tick、保存、队列阈值与检查周期可配置并在正式启动前校验；备份时效复用 DB-03 周期计算。告警以不可静默丢弃的 `Warn` 日志写入 `OPS_ALERT`，只在触发/恢复转换时记录；MySQL 部署不会误报 SQLite 备份缺失。该阈值只作为可调运营告警起点，不是压力容量承诺；用户后续自行实机观察。专项、全量及构建证据见 `Docs/Evidence/GATE-P5/ops-basic-01-monitoring-20260810/`；运维说明见 `Docs/OPS-BASIC-01-基础服务端监控与告警.md`。OPS-BASIC-02～04、PROTO-02/03、RELEASE-01～03 仍未完成，GATE-P5 保持开启。

**OPS-BASIC-02 收口记录（2026-08-10）**：共享 `CrashDiagnosticBundle` 在同一输出根目录内先写入并刷新 `.partial`，完成后再以目录移动发布；失败会清理半成品，同进程同组件只保留首个崩溃现场。PC、Android 和服务端正式宿主均接入未处理异常；Android 还覆盖游戏对象构造前的启动协调阶段，并在捕获前同步排空移动日志队列。诊断包包含每份现有日志脱敏后最后最多 64 KiB、程序版本、SEC-06 资源状态版本及摘要、宿主白名单配置摘要和异常；首次启动无接受状态时回退到随包资源索引，服务端清单随正式构建复制。JSON/键值秘密、Bearer/Basic、连接串、邮箱、IPv4 与用户主目录名均在落盘前遮蔽；不自动上传。专项、全量及五项目标构建证据见 `Docs/Evidence/GATE-P5/ops-basic-02-crash-diagnostics-20260810/`；使用边界见 `Docs/OPS-BASIC-02-崩溃诊断基础.md`。OPS-BASIC-03/04、PROTO-02/03、RELEASE-01～03 仍未完成，GATE-P5 保持开启。

**OPS-BASIC-03 收口记录（2026-08-10）**：正式服务端新增原子持久化的四项总闸，Administrator 可经 SEC-04 管理 HTTP 修改商城、服务端微端资源更新、活动和高风险账户操作，Operator 可只读查询；每次请求保留 `ADMIN_AUDIT`，成功变更把完整连续审计与状态一次原子提交，另以可靠 `Warn` 写入不含原因原文的 `OPS_KILL_SWITCH` 检索副本，日志副本失败不会伪报开关失败。状态和审计先写入、刷新并原子替换，成功后才发布不可变快照；损坏、缺字段、未知版本、审计代次不连续、审计重放与四闸状态不一致或不可读状态在监听和 Ready 前失败关闭。商城列表与两条购买路径、微端资源文件路径、攻城/魔龙主线程进度以及开户/改密/删角入口均读取同一快照；正在进行的攻城只在主线程停止，不从管理线程跨界修改游戏状态。服务端外部签名仓库的事务化发布仍属于 RELEASE-02，细粒度开关仍属于发布后 OPS-03。专项、全量及构建证据见 `Docs/Evidence/GATE-P5/ops-basic-03-kill-switch-20260810/`；运维说明见 `Docs/OPS-BASIC-03-Kill-Switch.md`。PROTO-02/03、RELEASE-01～03 仍未完成，GATE-P5 保持开启。

**OPS-BASIC-04 收口记录（2026-08-10）**：使用 Microsoft SBOM Tool 4.1.5 为服务端、PC 和 Android 六个真实 Release 工件生成 SPDX 2.2 发布侧车，记录 218 个包、6 个文件及 988 条依赖关系；另将 218 包纯依赖 SPDX、外部资源清单、许可证/NOTICE/EULA 正文及全部 217 个第三方生产包的实际版权/作者归属随 PC、服务端、Android APK 与 iOS 发布。依赖 SPDX 不含文件校验码、自引用或悬空引用，并由专项测试验证关系闭合。许可证自动识别 204/221 个唯一组件，12 个 `NOASSERTION` 项按包名和版本由专项测试锁定。初扫发现的 `log4net 3.0.3` 中危和 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 高危已分别升级到 3.3.2 和 2.1.12，Windows、Android、iOS 直接/传递依赖复扫原始 JSON 均为退出码 0、无漏洞包。`D:\ChuanQi\Crystal_monogame` 的素材、字体、FairyGUI、音频、地图和微端资源已按项目所有者的明确授权确认分类记录，不把 11GB 外部资源复制进 Git；资源签名继续复用 SEC-06。证据见 `Docs/Evidence/GATE-P5/ops-basic-04-license-audit-20260810/`；执行边界见 `Docs/OPS-BASIC-04-授权与依赖审计.md`。PERF-01/02 模拟并发与 PROTO-02/03 已于后续回补完成；RELEASE-01～03 仍未完成，GATE-P5 保持开启。

**PROTO-02/03 收口记录（2026-08-10）**：Android 正式构建已从编译 `Share/**/*.cs` 协议副本切换为直接链接 `Shared` 的 17 个协议源文件；PC 与 Server 继续通过项目引用消费同一 `Shared`。历史 `Share` 副本不删除、不修改，只保留给 `ShareProtocolCompat` 作为 PROTO-01 兼容差异夹具。新增受限的 `ProtocolManifestGenerator` 从实际 `Shared.dll` 和 C# 源生成包方向、ID、字段、枚举、读写 IL 与源文件摘要，源摘要先统一换行，CI 每次执行 `--verify`，漂移即失败；当前覆盖 145 个客户端包、275 个服务端包和 64 个公开枚举，并已在 LF 工作树副本复验。兼容矩阵固化 `wire-v1`、PC/Server Assembly `1.0.0.0`、Android Display `2.0.0`、PC 资源兼容版本 `1.0.0`、Android `2.0.0`、两端当前随包资源版本、SEC-06 签名清单最低版本门禁及服务端构建哈希白名单边界。专项 14/14、Base05 348/348、Android 正式宿主及 Android Shared/PC/Server Release 构建均 0 错误；证据见 `Docs/Evidence/GATE-P5/proto02-03-unification-20260810/`。RELEASE-01～03 仍未完成，GATE-P5 保持开启。

**RELEASE-01 收口记录（2026-08-10）**：APK 与资源索引已使用不可互换的独立生产密钥。资源侧生成当前 `resource-2026-a` 与下一 `resource-2026-b` 两把 ECDSA P-256 密钥，私钥仅保存在忽略的 Windows DPAPI CurrentUser 文件或 CI `production-signing` Environment Secret；SPKI 公钥和重叠序列窗口编译进客户端信任表。正式签名索引覆盖随包全部 261 个资源包，PC `1.0.0` 与 Android `2.0.0` 均验签通过。Android arm64 Release/AOT+Trim APK 使用独立 RSA 4096 keystore 签名，`apksigner` 验证 v2/v3、单签名者通过。`ReleaseSigningTool` 对资源私钥执行完整 P-256 PKCS#8 导入、签名后自验和敏感字节清零；本地 APK 口令通过 DPAPI 解密后只进入子进程环境，构建日志可原子更新而密钥输出仍拒绝覆盖。CI 工作流在缺秘密或三份密钥材料任意相同时失败关闭，仅在对应步骤取用 Secret，并始终清理临时 keystore/索引。T-07 与生产专项 11/11、Base05 全量 351/351；签名工具和 Server.Library Release 0 错误，PC Release 0 错误，Android 正式签名构建成功。运维边界见 `Docs/RELEASE-01-签名密钥与受保护签名.md`，证据见 `Docs/Evidence/GATE-P5/release01-signing-20260810/`。远端 Secret 实值由仓库管理员在 GitHub 外部配置，不在 Git 中保存或伪称已配置；事务发布、灰度/回滚与 APK 密钥灾难恢复仍分别属于 RELEASE-02/03，GATE-P5 保持开启。

**RELEASE-02 收口记录（2026-08-10）**：`Tools/Invoke-Release02.ps1` 已将 Base05 冒烟、PC/服务端发布、261 包确定性导出、RELEASE-01 资源签名与信任表复验、Android arm64 AOT+Trim 独立签名、真实 `apksigner` 复验、工件 SHA-256 清单、5% 分桶和 loopback 发布网关收口为一条命令。最终本地不可变版本 `release02-20260810-r3`（Sequence 4）保留 `release02-20260810-r2` 为上一健康版本；1097 项发布文件与 261 个签名资源包经脚本外复算均 0 缺失、0 摘要/大小不一致，完整一键 transcript 也在发布清单内，未残留半成品目录。网关按稳定 ClientId 做确定性 5% 分桶，实际 HTTP 已返回当前/上一版本；观测事件必须通过渠道根 DPAPI CurrentUser 保护的 256 位 Bearer 鉴权，并同时绑定当前 ReleaseId 与重新计算的灰度 cohort，稳定版、旧版迟到、回滚后事件和超过 4096 字节正文均失败关闭。幂等事件写入即自动聚合和评估，连续 3 次致命崩溃立即单向回滚，100 样本后更新失败率大于 2% 或启动崩溃率大于 1% 自动回滚，重复回滚不会把坏版本重新切回。回滚前严格复验上一版本全部工件和签名索引。PC 与 Mono/Android 都先集齐签名队列全部包，再把全部资源、版本状态和清空后的队列一次事务提交；Mono/Android 额外逐包核对 Bundle 元数据摘要与当前签名摘要，旧 Bundle 不会进入事务。共用事务根持有跨进程文件锁、同目录原子替换、逐文件 SHA-256 后验和反向恢复，启动时先处理未完成事务。最终专项 17/17、Base05 全量 368/368；完整证据见 `Docs/Evidence/GATE-P5/release02-pipeline-20260810/`，运维说明见 `Docs/RELEASE-02-事务发布与灰度回滚.md`。本轮没有上传官网、商店、CDN 或真实玩家渠道；RELEASE-03 设备验收与 APK 密钥灾难恢复仍未完成，GATE-P5 保持开启。

**RELEASE-03/GATE-P5 收口记录（2026-08-10）**：Android 正式远端更新改为只请求 `bootstrap-package-index.signed.json`；逍遥等效设备首次运行复现并关闭两个整版发布缺口：签名全集未到齐时不再落入旧单包兼容路径，远端 `core-startup` 也不再覆盖用户运行时 `Mir2Config.ini`。干净安装真实下载 `core-startup + fui-retro`：仅首包到达时队列保持、无 `Processed`；下载中强停后 `.part` 从 1638400 bytes 保留到 7995392 bytes，重启后以发布网关 HTTP 206 Range 继续增长至 36896768 bytes；全集到齐后两个目录同批标记 `release-applied`，版本记录使用同一提交时间，队列清空且用户 PackageRepo 保留；实际篡改 Zip 的专项验证确认坏摘要失败关闭。不同签名覆盖被系统拒绝，同一 RSA 4096 密钥新装/覆盖升级均成功且应用数据保留；最终 arm64 AOT+Trim APK 使用 v2/v3、单签名者，AOT 107/107。Debug、Release、Trim-only 与 AOT+Trim 四态构建通过；正式态完成前后台、锁屏、分辨率和网络断开/恢复，FairyGUI 全包加载、11 张贴图预热及正式协议登录握手无崩溃。现有签名工具新增 PBKDF2-SHA256 600000 次 + AES-GCM 加密恢复包，异常路径清零全部秘密；实际生产 keystore 错误口令失败、正确导入摘要一致，恢复出的 DPAPI 材料完成正式构建并由 apksigner 原始输出确认同一证书。专项 22/22、Base05 369/369；原始命令证据见 `Docs/Evidence/GATE-P5/release03-android-20260810/`，运维边界见 `Docs/RELEASE-03-Android生命周期与设备验收.md`。容量验证按项目决定使用真实协议模拟客户端保持目标连接数，不要求数百台实体设备或上线账号；实体手机基带来电、真实蜂窝/Wi-Fi 射频、刘海/输入法与手感留项目所有者交付后观察，不伪报为自动化结果、不再阻塞开发门禁。至此 RELEASE-03 完成，GATE-P5 关闭；P0～P5 文档开发任务全部完成，P6/P7 保持发布后路线图。

### P6 脚本化赛季活动（4~6 周，发布后路线图）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| EVENT-01 | 分层实现：Script 只管规则/条件/倍率/奖励配置/事件 Hook；**Core Activity Service** 管状态机/计分/幂等/审计；SQLite 持久化成绩/领奖状态/结算结果；客户端只展示不判定 | GATE-P5 | 服务端权威校验生效 |
| EVENT-02 | 状态与幂等：`ActivityId`/`SeasonId`/UTC 时间；未开始/进行中/结算中/已结束；重启恢复；计分幂等键；防重复领奖；补发/撤销；活动关闭开关（联动 OPS-BASIC-03） | EVENT-01 | 重放测试通过；重启恢复验证通过 |
| EVENT-03 | 移动端活动页 UI + 公告 | ANDROID-01..07 | PC + Android 一致 |

**退出条件**：活动从"开→跑→结→发奖"全链路热更上线，结算幂等，重启不丢状态。

### P7 运维后台与高级可观测性（3~4 周，发布后深化）

| 编号 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| OPS-01 | 高级仪表盘：在线/地图分布/保存耗时 p95/p99/Tick p95/p99/GC/内存/断线率/保存失败率/队列积压 + 极简前端 | OPS-BASIC-01/PERF-00 | 运营可看核心指标；仅回环/内网 + 独立凭据 |
| OPS-02 | 崩溃诊断包升级：扩展 OPS-BASIC-02 收集范围（含线程栈/网络状态） | OPS-BASIC-02 | 崩溃现场可离线定位 + 自动上报 |
| OPS-03 | Feature Flag / Kill Switch 完善：扩展 OPS-BASIC-03 支持活动/商城/高风险功能精细开关 + 审计 | OPS-BASIC-03 | 开关生效 + 审计 |

---

## 5. 稳定性观测参考

> 2026-08-10 产品决定：开发阶段执行模拟协议并发，真机体验和长时 soak 由用户后续验证。300 连接/100 登录会话短时阶段验证已完成；下表其余字段保留为 PERF-00 可观测口径和发布后排障参考。

| 指标 | 目标 | 说明 |
|---|---|---|
| 并发规模 | 300 连接 / 100 活跃协议会话 | 30 秒模拟验证完成；不等同 100 个战斗角色 |
| Tick p99 | 由实机观察记录 | 主循环耗时 |
| 保存快照交接最大耗时 | 由实机观察记录 | 主线程只做交接，但快照捕获本身也要限时 |
| 保存耗时 p95/p99 | ≤ 可接受上限（后台化后显著下降） | 专用写线程提交 |
| GC pause | ≤ 可接受上限 | 压测采样 |
| 内存增长斜率 | ≤ 72h 不超阈值 | 24/72h soak |
| 断线率 | ≤ 可接受上限 | 300 连接/100 活跃下采样 |
| 保存失败率 | 0（应有告警） | 失败即告警 |
| 更新失败率 | ≤ 可接受上限 | 发布/灰度期间 |
| 客户端崩溃率 | ≤ 可接受上限 | 发布后监控 |
| 未处理异常数 | ≤ 允许值 | soak 期间 |
| Soak | 24h / 72h 无幽灵崩溃 | 错误预算内 |

> G1"PC 无回滚"改为"PC 无阻塞级功能回归"——回滚是发布安全能力，不是稳定性目标。

---

## 6. 里程碑与工期（发布 / 发布后分离）

### 6.1 Android 正式发布里程碑（P0–P5，本轮）

| 阶段 | 最小 | 最大 |
|---|---|---|
| P0 可复现基线 + .NET 10 迁移 | 4 | 6 |
| P1 Android 真机闭环 + 协议盘点 | 5 | 6 |
| P2 安全改造 | 4 | 6 |
| P3 SQLite 生产化 | 3 | 5 |
| P4 性能优化 | 4 | 6 |
| P5 协议统一 + 发布流水线 + 发布前能力 | 4 | 6 |
| **发布里程碑合计** | **24** | **35** |
| **含 10% 缓冲** | **26** | **39** |

**2026-08-09 滚动预测**：P0 已完成；P1 的服务、资源、账号、APK 和逍遥实机等效环境已打通，当前剩余是七项业务闭环的具体缺口。若坐骑/活动缺陷不扩散，预计 **3~7 个工作日**关闭 P1；P1 关闭前，正式发布剩余总工期仍保守维持 **14~23 周未缓冲 / 16~25 周含缓冲**，不因一次登录或窗口通过提前压缩。签名密钥和后续发布环境仍是 P5 前置，每个 GATE 关闭后重新估算。

### 6.2 发布后路线图（P6–P7，后续立项）

| 阶段 | 最小 | 最大 |
|---|---|---|
| P6 脚本化赛季活动 | 4 | 6 |
| P7 运维后台 | 3 | 4 |
| **发布后合计** | **7** | **10** |
| **含 10% 缓冲** | **8** | **11** |

> v1.4 修正：正式发布里程碑（P0–P5）与发布后路线图（P6–P7）分离计算，不再混为 34~50 周。**Android 正式上线门禁 = GATE-P5。**

---

## 7. 完成定义（DoD）

| 维度 | 验收 |
|---|---|
| 运行时 | 正式发布基线 = .NET 10 LTS；无 Preview SDK/Runtime/NuGet；Android 最低 API 24 |
| 协议 | `Shared.Protocol` 唯一源 + **自动生成 manifest 无漂移**；三端（PC+Android）序列化回归绿；`Enums` 协议段无分叉 |
| 安全 | 凭据不通过未加密网络传输；公网无 V1 明文登录；无弱哈希；无默认 GM 密码；无密码落盘；管理端独立凭据 + 仅回环/内网 |
| 数据 | SQLite WAL + 单写者 + RPO≤5min（配置强校验）/RTO≤30min + 备份恢复演练通过 |
| 移动端 | 目标功能矩阵全绿；无未知包；性能同场景前后数据验收 |
| 发布 | 签名 APK + 签名资源清单 + 灰度/回滚 + 兼容矩阵 + 事务化更新 + 生命周期验收 |
| 运营 | 最小监控/崩溃诊断/Kill Switch/授权审计（OPS-BASIC-01..04）P5 门禁前就绪 |
| 工程 | `.sln`/`.slnf` 与磁盘同步；版本集中；无幽灵工程；CI 矩阵全绿 |

---

## 8. 风险登记

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| net11→net10 迁移（Maui/AOT/包降版） | 中 | **高（阻塞发布）** | BASE-06 的 P0 模拟器四态验证；arm64 真机四态纳入 RELEASE-03 设备验收 |
| iOS 无 macOS 环境 | 中 | 高 | 仅隔离 TFM，不阻塞 Windows/Android（ADR-3） |
| 协议收口波及移动端 | 高 | 中 | C# 为源 + manifest 自动生成 + 兼容测试 + 源码链接过渡 |
| 安全改造影响现有登录 | 中 | 高 | V1 独立端口限期兼容 + 公网默认关闭明文；老账号透明升级 |
| SQLite 增量写丢档 | 中 | 高 | 先测量→后台化→验证→再脏标记 |
| RPO 配置被改大 | 低 | 高 | DB-05 生产强校验 1~5min + 故障注入 |
| 外部资源（QQ 群/本机源）不可复现 | 高 | 中 | BASE-02 本机可复现 + 摘要清单 + 哈希；BASE-02b 资源镜像（独立后续） |
| 多会话并行导致冲突或越门禁 | 高 | 高 | 门禁间串行；门禁内按依赖与文件所有权并行；单一集成会话合并 |
| PowerPacks 迁移风险 | 低 | 中 | 盘点后保留，替换单独立项（ADR-10） |

---

*原始里程碑保留单会话全职估算作为保守基线；当前执行采用“门禁间串行、门禁内低冲突任务多会话并行”，滚动工期见 §6.1。阶段门禁 GATE-P0..P5 不可绕过。*
