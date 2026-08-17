# LFENV-16 单版本完整切片阻断记录（未关闭）

## 验证范围

- 任务：`LFENV-16 单版本完整切片`。
- 固定来源：`01酷明传奇` 只验证原版命格系统；`封神` 只验证原版法宝系统。
- 隔离约束：两套版本的脚本、数据库、地图、奖励与断言没有交叉拼接。
- 本记录不关闭 LFENV-16，也不代表 LFENV-17、LFENV-18、LFENV-19 或 `GATE-ALL` 已完成。
- 最终 Spec 复审确认两个阻断：酷明源包缺少 E1 依赖，隔离占位映射不能作为原样冷启动证据；封神原版法宝收录测试只覆盖收录核心，未形成同一原版玩法的完整 E2 闭环。

## 可运行工件

- 原样 Envir 严格预检、阶段闭环与阻断审计：`Tests/Base05.Tests/LingFengCompleteSliceTests.cs`。
- 隔离候选配置：`Configs/LingFengEnvirSlice/`。
- 人物进度与客户端表现状态：`src/Server/Server/MirDatabase/LingFengCharacterProgress.cs`、`src/Shared/Shared/Data/LingFengClientPresentationState.cs`。
- 整服文本、CSV、宏、镜像地图、副本、运行期列表和脚本引用接缝：`src/Server/Server/Scripting/LingFeng*.cs`。
- PC 与 Android 共用协议和状态模型：`src/Shared/Shared/Packet.cs`、`src/Shared/Shared/ServerPackets.cs` 及两端客户端实现。

## 专项结果

`LingFengCompleteSliceTests` 当前保留九类阶段场景；其中通过用例证明已实现接缝，阻断审计用例证明源资源仍不满足门禁：

1. 封神法宝精简包通过严格脚本预检；
2. 合成法宝验证候选通过严格预检且 Setup 与回滚对称；
3. 酷明命格完整目录未知命令为零，跨玩法引用保留为外部依赖；
4. INI 唯一全局跳转进入真实执行，缺失外部页进入 E2 依赖；
5. 酷明原样 Envir 通过严格 E1 脚本预检；
6. 酷明原样机器人脚本由真实 `NPCScript` 运行时完成解析；
7. 酷明原样 Envir 只读加载源数据库与实际地图，逐项核对 E1 manifest，并确认真实缺口会阻断；
8. 封神原版法宝收录脚本直接完成扣币、回收、登记、保存和重启闭环；
9. 合成法宝验证切片完成登录、NPC、任务、击杀、提交、奖励、保存和重启闭环。

第 8 项直接复制并执行封神原版 `法宝收录.txt`、`法宝图鉴.csv` 与完整 `属性脚本.txt`，读取封神原数据库中的 `【法宝】乾坤圈`，断言 200 游戏晶格扣除、背包物品回收、收录标记与 SQLite 重启持久化。`HCALL` 已接通指定在线人物与唯一原页面，并有双人物隔离回归；数字开头的 `$` 命名变量与动态 `KILLMONEXPRATE` 已通过原版页执行，但完整 `@属性计算` 仍由测试明确锁定数据库字段/空装备上下文、变量类型和 CSV 查找等多类独立阻断。第 9 项只验证跨领域基础接缝，同时断言：

- 任务标记、掉落、提交和奖励均进入真实领域链；
- 金币净变化为 200，可由测试账本核对；
- 保存与重建人物对象后进度仍存在；
- PC 与 Android 的单个 `NPCResponse` 协议字节一致；尚未覆盖原版玩法全部状态与奖励消费。

酷明真实资源审计不修改源目录，也不再构造占位资源：

- `LFENV-ROOT-0002` 的严格脚本候选和机器人运行时解析通过。
- 审计直接读取源 `ApexM2.DB` 的全部物品/怪物名称与编号，并只枚举源 `MAP` 的实际 `.map` 文件；manifest 中不存在的项全部进入 `Missing`。
- 2026-08-17 只读诊断共确认 `1493` 个 E1 缺失引用：物品名 `987`、物品编号 `10`、怪物 `95`、地图 `401`；该规模属于源包完整性阻断，不能从其他版本拼装后冒充酷明原样输入。
- 测试显式断言审计失败且每个缺失项在真实资源集合中确实不存在；不复制替代地图，不新增物品或怪物，不关闭数据库检查，也不把阻断冒充冷启动通过。

封神原版法宝输入为 `LFENV-ROOT-0018`：185 个文件，清单摘要 `32AE59C3A0C41955019C84E15E8DEF030554655BE1606A8D4CC770A16BB406D1`，内容摘要 `8A5FC2F237A48056FA1DA2EFD7D6C5AB4B52521B9611CB0FA89BA070FF4139A0`；原版闭环同样在执行前后断言源目录摘要一致。

## 门禁命令与结果

### Base05 当前阶段全量

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName!~LingFengMultiVersionMatrixTests" --logger "trx;LogFileName=lfenv16-full-gate-current.trx" --logger "console;verbosity=minimal"
```

当前结果为 `1056/1056` 通过，`0` 失败，`0` 跳过，用时 `5 分 53 秒`；TRX 为 `Tests/Base05.Tests/TestResults/lfenv16-full-gate-current.trx`。`LingFengMultiVersionMatrixTests` 是用户工作区中属于 LFENV-17 的下一阶段红测，本轮按当前阶段边界排除且未修改。

### Windows 解决方案构建

```powershell
dotnet build LyoCrystal.Windows.slnf --configuration Release --no-restore
```

结果：`0` 错误；`517` 条既有编译警告，用时 `1 分 12.17 秒`。

### 协议清单漂移

```powershell
dotnet run --project Tools/ProtocolManifestGenerator/ProtocolManifestGenerator.csproj --configuration Release -- --verify Docs/generated/protocol/protocol-wire-manifest.generated.json
```

结果：通过，生成清单与代码协议一致。

### 差异格式

```powershell
git diff --check
```

结果：无空白错误；仅报告工作区下一次写入时的 LF/CRLF 转换提示。

### 引擎说明书严格构建

```powershell
Push-Location Manual/Engine
.\.venv\Scripts\python.exe -m mkdocs build --strict --clean --config-file mkdocs.yml
Pop-Location
```

结果：通过，站点在 `13.38 秒` 内完成构建；Material for MkDocs 的上游版本提示不属于 MkDocs 严格模式错误。

### 网关正常观察性能基线

性能测试只使用不触发违规证据构造的正常 Observe 流量，保留 `5 微秒` 门槛不变；同一性能用例在当前主机负载下连续四次通过。违规证据与限流路径继续由独立测试覆盖。

## E1 / E2 结论

- E1 阻断：酷明原样 Envir 的严格候选构建和机器人真实解析通过；真实资源审计确认源数据库与地图缺少 manifest 所列依赖。补齐正式资源前不执行或声明原样生产冷启动。
- E2 阻断：封神原版法宝收录脚本已直接完成扣币、回收、收录、保存与重启，`HCALL` 定向页面派发也已实现；数字开头的 `$` 命名变量与动态经验倍率已接通，但完整 `@属性计算` 仍有数据库字段/空装备上下文、变量类型和 CSV 查找等独立缺口，同一原版流程也尚未覆盖登录、NPC、怪物掉落、完整奖励及 Android 实际显示。合成切片不能与原版局部流程拼接为完整玩法。
- 输入完整性：酷明与封神源目录均在运行前后重算摘要并保持一致；全部写入仅发生在隔离目录和隔离数据库。
- 未声明：未取得的原 WIL/Pak 表现资源、十版本矩阵、53 根全语料、真实服灰度与正式发布仍属于后续任务。

## 回滚

- 禁用 `Configs/LingFengEnvirSlice/` 对应候选配置并恢复上一成功快照；候选发布失败时现有 Provider 保留上一完整快照。
- 数据模型升级保持向后读取；新状态没有有效值时按旧版本默认值运行。
- 本任务形成独立提交，主线合并前由项目所有者确认。
