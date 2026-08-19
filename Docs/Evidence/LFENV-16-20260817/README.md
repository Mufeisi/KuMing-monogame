# LFENV-16 单版本完整切片阶段收口记录（暂停）

## 验证范围

- 任务：`LFENV-16 单版本完整切片`。
- 固定来源：`01酷明传奇` 只验证原版命格系统；`封神` 只验证原版法宝系统。
- 隔离约束：两套版本的脚本、数据库、地图、奖励与断言没有交叉拼接。
- 本记录在当前可复现资源边界内收口并暂停 LFENV-16，不代表完整 E2、LFENV-17、LFENV-18、LFENV-19 或 `GATE-ALL` 已完成。
- 酷明数据缺项已确认为运行时安全降级并保持结构化报告；封神原版数据库、地图和完整资源尚未纳入仓库可复现资源基线。封神完整 E2 不以合成切片、其他版本数据库或占位资源替代，本轮不继续扩展兼容代码。

## 可运行工件

- 原样 Envir 严格预检、阶段闭环与阻断审计：`Tests/Base05.Tests/LingFengCompleteSliceTests.cs`。
- 隔离候选配置：`Configs/LingFengEnvirSlice/`。
- 人物进度与客户端表现状态：`src/Server/Server/MirDatabase/LingFengCharacterProgress.cs`、`src/Shared/Shared/Data/LingFengClientPresentationState.cs`。
- 整服文本、CSV、宏、镜像地图、副本、运行期列表和脚本引用接缝：`src/Server/Server/Scripting/LingFeng*.cs`。
- PC 与 Android 共用协议和状态模型：`src/Shared/Shared/Packet.cs`、`src/Shared/Shared/ServerPackets.cs` 及两端客户端实现。

## 专项结果

`LingFengCompleteSliceTests` 当前保留十类阶段场景；其中通过用例证明已实现接缝，阻断审计用例证明源资源仍不满足门禁：

1. 封神原版五行灵珠已形成登录、NPC、击杀掉落、合成奖励与重启持久化的待验证链，但严格候选先被原版常量冲突失败关闭；
2. 封神原版法宝收录脚本直接完成扣币、回收、登记、保存和重启闭环；
3. 合成法宝验证切片完成登录、NPC、任务、击杀、提交、奖励、保存和重启闭环；
4. 合成法宝验证候选通过严格预检且 Setup 与回滚对称；
5. 酷明原样 Envir 通过严格 E1 脚本预检；
6. 酷明原样机器人脚本由真实 `NPCScript` 运行时完成解析；
7. 酷明原样 Envir 只读加载源数据库与实际地图，逐项核对 E1 manifest，并确认真实缺口如实报告且不误报满足；
8. INI 唯一全局跳转进入真实执行，缺失外部页进入 E2 依赖；
9. 酷明命格完整目录未知命令为零，跨玩法引用保留为外部依赖；
10. 封神法宝精简包通过严格脚本预检。

第 1 项直接复制封神原版 `Constant.ini`、`QManage.txt`、混沌城五行灵珠 NPC、五行灵珠功能脚本与真实掉落链，并从原 `ApexM2.DB` 读取物品和怪物、从原 `Mx076.map` 加载地图、从原 `MapInfo.txt` 核对 `Zc4|Mx076` 逻辑/物理地图映射。统一 `LingFengDefineExpander` 现已识别原版 `#名称#` 宏，并在进入运行解释器前正确拒绝 `#T装备首暴#`、`#J封神币限购#`、`#逻辑_首杀标识#` 三组异值重复定义。规格禁止以“末定义覆盖”、摘取单个 551 常量或降级 `DependencyLevel` 绕过该候选门禁，所以后续经济与持久化链当前不作为 E2 通过证据。

第 2 项直接复制并执行封神原版 `法宝收录.txt`、`法宝图鉴.csv` 与完整 `属性脚本.txt`，读取封神原数据库中的 `【法宝】乾坤圈`，断言 200 游戏晶格扣除、背包物品回收、收录标记与 SQLite 重启持久化。`HCALL` 已接通指定在线人物与唯一原页面，并有双人物隔离与 `#SAY` 追加回归；数字开头的 `$` 命名变量与动态 `KILLMONEXPRATE` 已通过原版页执行，但完整 `@属性计算` 仍由测试明确锁定数据库字段/空装备上下文、变量类型和 CSV 查找等多类独立缺口。第 1 项目前只是五行灵珠原版 E2 的待验证链，不能用第 2 项的局部通过替代。第 3 项只验证跨领域基础接缝，同时断言：

- 任务标记、掉落、提交和奖励均进入真实领域链；
- 金币净变化为 200，可由测试账本核对；
- 保存与重建人物对象后进度仍存在；
- PC 与 Android 的单个 `NPCResponse` 协议字节一致；尚未覆盖原版玩法全部状态与奖励消费。

酷明真实资源审计不修改源目录，也不再构造占位资源：

- `LFENV-ROOT-0002` 的严格脚本候选和机器人运行时解析通过。
- 审计直接读取源 `ApexM2.DB` 的全部物品/怪物名称与编号，并只枚举源 `MAP` 的实际 `.map` 文件；manifest 中不存在的项全部进入 `Missing`。
- 2026-08-18 只读诊断共确认 `901` 个 E1 缺失引用：物品名 `783`、物品编号 `9`、怪物 `95`、地图 `14`；该规模属于源包完整性阻断，不能从其他版本拼装后冒充酷明原样输入。审计已按生产语义识别 `MapInfo.txt` 地图别名、原版 `MonUseItems` 的 DEL 填充终止符，并将酷明大量使用的 `GIVE/TAKE 金币`路由到账户金币账本而非物品数据库，旧的 `1493` 计数因此作废。
- 测试显式断言审计失败且每个缺失项在真实资源集合中确实不存在；不复制替代地图，不新增物品或怪物，不关闭数据库检查，也不把阻断冒充冷启动通过。

封神原版法宝输入为 `LFENV-ROOT-0018`：185 个文件，清单摘要 `32AE59C3A0C41955019C84E15E8DEF030554655BE1606A8D4CC770A16BB406D1`，内容摘要 `8A5FC2F237A48056FA1DA2EFD7D6C5AB4B52521B9611CB0FA89BA070FF4139A0`；原版闭环同样在执行前后断言源目录摘要一致。

## 门禁命令与结果

### Base05 当前阶段全量

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --configuration Release --no-build --logger "trx;LogFileName=lfenv16-closeout-20260819.trx" --logger "console;verbosity=minimal"
```

结果：`1064` 通过、`0` 失败、`1` 跳过，共 `1065` 项，耗时 `2 分 32 秒`。唯一跳过项为封神原版五行灵珠完整 E2；该测试已显式标记为外部资源阻塞，只有原版数据库、地图、脚本和客户端资源进入可追踪资源基线后才允许重新启用。重复宏继续按 `LFENV16-DEFINE-002` 失败关闭，不采用“后定义覆盖”放宽生产语义。LFENV-17 多版本矩阵测试已从本轮工作树隔离保存，未进入该计数。

### 仓库资源基线

```powershell
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope Repository
```

结果：通过；仓库内资源均为 `OK`，不属于 Repository 范围的外部资源按规则显示为 `SKIP`，不据此声明封神原版资源已验证。

### Release 解决方案构建

```powershell
dotnet build LyoCrystal.Server.slnf --configuration Release --no-restore
dotnet build LyoCrystal.Windows.slnf --configuration Release --no-restore
dotnet build LyoCrystal.Launcher.slnf --configuration Release --no-restore
```

结果：三个命令均以退出码 `0` 完成，无编译错误；输出保留既有编译警告。

### 协议清单漂移

```powershell
dotnet run --project Tools/ProtocolManifestGenerator/ProtocolManifestGenerator.csproj --configuration Release -- --verify Docs/generated/protocol/protocol-wire-manifest.generated.json
```

结果：通过，生成清单与代码协议一致。

### 差异格式

```powershell
git diff --check
```

结果：无空白错误。

### 引擎说明书严格构建

```powershell
Push-Location Manual/Engine
& "$env:TEMP\lyocrystal-manual-venv\Scripts\python.exe" -m mkdocs build --strict --clean --config-file mkdocs.yml
Pop-Location
```

结果：通过；Material for MkDocs 的上游版本提示不属于 MkDocs 严格模式错误。

### 网关正常观察性能基线

性能测试只使用不触发违规证据构造的正常 Observe 流量，保留 `5 微秒` 门槛不变；同一性能用例在当前主机负载下连续四次通过。违规证据与限流路径继续由独立测试覆盖。

## E1 / E2 结论

- E1 结论：酷明原样 Envir 的严格候选构建和机器人真实解析已验证；真实资源审计继续如实报告数据缺项，已证明属于 no-op/判否路径的缺项不再阻断启动。
- E2 阻断：封神原版数据库、地图和完整资源未进入仓库可复现资源基线，原版完整玩法测试暂停；已实现的局部接缝和合成切片不能拼接为完整 E2 证据。
- 输入完整性：既有酷明/封神整目录审计用例会在运行前后重算各自已纳入范围的源摘要；新增五行灵珠用例逐文件核对复制前后 SHA-256，但尚未形成封神完整源根的新摘要证据。全部测试写入仅发生在隔离目录和隔离数据库。
- 未声明：未取得的原 WIL/Pak 表现资源、十版本矩阵、53 根全语料、真实服灰度与正式发布仍属于后续任务。

## 回滚

- 禁用 `Configs/LingFengEnvirSlice/` 对应候选配置并恢复上一成功快照；候选发布失败时现有 Provider 保留上一完整快照。
- 数据模型升级保持向后读取；新状态没有有效值时按旧版本默认值运行。
- 本任务形成独立提交，主线合并前由项目所有者确认。
