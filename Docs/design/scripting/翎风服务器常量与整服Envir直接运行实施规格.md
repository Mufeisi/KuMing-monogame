# 翎风服务器常量与整服 Envir 直接运行实施规格

> 状态：已接受（LFENV-01、LFENV-02 已实施并验证）
>
> 负责人：项目所有者 / Codex
>
> 最后复核：2026-08-16
>
> 事实源：翎风原始说明书、用户提供的服务器常量表、`D:\ChuanQi\服务端` 只读语料、LyoCrystal 当前代码与测试
>
> 前置规格：[翎风 TXT 脚本兼容迁移实施规格](翎风TXT脚本兼容迁移实施规格.md)
>
> 取代关系：不取代前置规格；本文负责服务器只读常量、完整 Envir 领域配置和整服直接运行目标

## 1. 决策摘要

本文把后续兼容目标从“继续补若干 TXT 命令”升级为两个可验证的产品目标：

1. **翎风服务器常量兼容**：脚本可直接使用 `<$USERNAME>`、`<$SERVERNAME>`、`<$KILLMONNAME>` 等运行时只读占位符，并在人物、英雄、物品、战斗、宠物、行会/攻城、服务器等正确上下文中得到与翎风一致或明确受限的值。
2. **整服 Envir 兼容**：把选定翎风版本的 `Mir200\Envir` 原样复制到隔离的 LyoCrystal 目标环境后，不修改源文件即可通过严格预检、冷启动和代表性玩法探针；未知命令、未知常量、未归属文件和静默跳过均为零。

“服务器常量”与 `#DEFINE` 自定义常量不是同一能力：

| 名称 | 示例 | 值来源 | 是否可写 | 本文范围 |
|---|---|---|---|---|
| 服务器常量/只读占位符 | `<$LEVEL>`、`<$MAPTITLE>` | 当前人物、事件、地图或服务器运行状态 | 否 | 核心范围 |
| 自定义常量 | `#DEFINE $活动地图$ 3` | `Defines` 文件 | 否 | 后续独立任务，只保留兼容 seam |
| 脚本变量 | `P0`、`U.DropRate` | 脚本运行时变量模块 | 是 | 已有模块，本文不重造 |
| 客户端实时变量 | `$$GAMEGOLD` | 客户端 UI 绑定 | 否 | 需要协议/UI 的独立阶段 |

## 2. 已核实基线

### 2.1 当前仓库

- 已有物理 TXT Provider、组合来源、严格快照、热重载、执行预算、统一变量模块和基础系统 Hook。
- 当前兼容清单有 496 项：B 51、C 10、D 327、E 83、X 25；B/C 只代表既定目标范围，不代表整服兼容。
- 当前只读占位符集中在 `NPCSegment.ReplaceValue` 的大 `switch` 中；人物入口约 47 个分支，预检白名单约 51 个名称。
- 用户提供的服务器常量表初步抽取出 281 个唯一写法；与当前人物 `switch` 按名称或函数前缀直接命中的只有 19 个，另有 262 个需要新增、映射、受限替代或明确排除。
- 名称命中不等于语义通过。例如金币、游戏币、坐标、装备槽位和英雄字段必须逐项核对 LyoCrystal 数据模型，不能只靠同名判断。

附件中按名称直接命中的 19 项是：

```text
BELT、BOOTS、DATE、GAMEGOLD、GUILDNAME、GUILDWARFEE、HELMET、HP、LEVEL、
MAP、MAXHP、MAXMP、NECKLACE、PKPOINT、RING_L、RING_R、USERCOUNT、USERNAME、WEAPON
```

当前代码另有 `CLASS/MAPNAME/X_COORD/Y_COORD/CREDIT/MP` 等 LyoCrystal 名称，但附件使用 `JOB/MAPTITLE/X/Y/CREDITPOINT` 等翎风名称；它们应登记为待验证别名，不能计入已兼容。262 个初步缺口按依赖分组：

| 缺口组 | 代表名称 | 实施去向 |
|---|---|---|
| 人物基础属性 | `AC/MAXAC/DC/MAXDC/EXP/MAXEXP/HIT/SPD/LUCK` | P0 Player Adapter |
| 地图与时间 | `MAPTITLE/X/Y/FBMAP/YEAR/HOUR/DATETIME` | P0 Player/Clock Adapter |
| 装备与时装 | `DRESS/ARMRING_L/CHARM/SHIELD/FASHION*` | P0/P4 Equipment Adapter |
| 货币与服务器 | `GOLDCOUNT/GAMEPOINT/GAMEDIAMOND/SERVERNAME` | P0 Server Adapter |
| 事件与当前物品 | `KILLMONNAME/CURITEMNAME/USEITEMNAME/SCRIPTPARAM*` | P1 Trigger Adapter |
| 战斗目标 | `ATTACKMONSTER_*/STRUCKHP/PKPOWER/CURRRUSEMAGICID` | P1 Combat Adapter |
| 英雄 | `H.LEVEL/H.EXP/H.KILLMONNAME/H.ATTACKMONSTER_*` | P2 Hero Adapter 或 E |
| 宝宝与宠物 | `SLAVE*/Pet.*` | P2 Pet Adapter |
| 行会与攻城 | `RANKNAME/CASTLENAME/OWNERGUILD/LISTOFWAR` | P3 Guild/Conquest Adapter |
| 高级系统 | `NH/NGEXP/CRYSTALEXP/GODBLESSITEM*/JEWELRYITEM*` | P4 领域模型或 E |
| 安全敏感 | `USERID/MACHINEID/SERVERIP/GameDirectory/BANKACCOUNT*` | 安全审查、C 或 X |

### 2.2 真实语料

2026-08-16 对 `D:\ChuanQi\服务端` 做了只读初扫：

- 总文件约 144,394 个；
- 识别出 53 个 `Mir200\Envir*` 根；
- 2026-08-16 最终画像快照中 Envir 文件 68,140 个；相较设计初扫增加 1 个零字节 TXT，目录必须以生成清单和哈希为准；
- 主要为 57,890 个 `.txt`、9,372 个 `.ini`，另有 `.sav/.prc/.csv/.dat/.bak` 等运行或领域数据；
- `<$...>` 原始匹配约 638,781 次；动态变量表达式和编码混用会放大唯一文本数量，因此必须在正式盘点时按语法族归一化，不能把原始唯一字符串直接当常量数量。

高频样本包括：

| 常量或语法族 | 初扫次数 | 说明 |
|---|---:|---|
| `<$USERNAME>` | 24,831 | 人物显示主路径 |
| `<$USERID>` | 5,531 | 账户标识，涉及隐私策略 |
| `<$MAP>` | 5,141 | 地图代码 |
| `<$KILLMONNAME>` | 4,183 | 击杀事件上下文 |
| `<$SERVERNAME>` | 3,161 | 服务器配置 |
| `<$CURRRTARGETNAME>` | 3,057 | 战斗/击杀上下文 |
| `<$H.KILLMONNAME>` | 2,799 | 英雄击杀上下文 |
| `<$MAPTITLE>` | 2,501 | 地图标题 |
| `<$CURITEMNAME>` | 2,232 | 物品触发上下文 |
| `<$X>` / `<$Y>` | 1,724 / 1,722 | 人物坐标别名 |
| `<$LEVEL>` | 1,631 | 人物等级 |
| `<$GOLDCOUNT>` | 1,191 | 金币 |

这些数字只是规划基线。正式清单必须记录编码、来源版本、上下文、动态索引和结构化等价次数。

### 2.3 代表性版本池

首轮不能只验证一份 Envir。至少覆盖以下不同规模和玩法家族：

| 样本 | 初扫文件数 | 主要价值 |
|---|---:|---|
| `无尽` | 6,699 | 最大综合样本，压力与长尾命令 |
| `迦楼/脚本` | 4,261 | 多版本并存、任务脚本库 |
| `雪国恋歌` | 3,962 | 地图/活动/新区配置 |
| `北荒` | 3,667 | 综合玩法与通区脚本 |
| `风隐` | 3,512 | 大型内容样本 |
| `封神` | 3,039 | 法宝玩法与提取包，可做完整玩法切片 |
| `符文` | 3,021 | 大量命名变量、装备集合与常量 |
| `逐鹿` | 2,778 | 综合回归样本 |
| `暴雪` | 2,770 | 传统 Defines 与 NPC 内容 |
| `01酷明传奇` | 2,370 | 既有迁移基线，便于与历史结果对照 |

正式阶段先按内容摘要去重，再从不同家族选择样本。副本、通区充值、开区覆盖和精简提取包不能与完整版本重复计权，但要作为专项组合测试。

## 3. “复制 Envir 零报错直接运行”的严格定义

### 3.1 必须同时满足

1. 源 Envir 只读复制，迁移过程不改写源目录内任何字节。
2. 所有输入文件都被分类为脚本、领域配置、运行数据、客户端依赖或明确拒绝；未归属文件数为 0。
3. UTF-8、UTF-8 BOM、CP936/GBK 和换行格式能被确定性读取；无法解码文件数为 0。
4. 未知命令、未知检测、未知动作、未知触发、未知服务器常量、缺失标签、缺失包含、循环包含、重复逻辑 Key 均为 0。
5. 不允许用忽略未知行、吞异常、默认返回空字符串或关闭严格模式制造“零报错”。
6. 每个被接受的领域文件都有唯一 Provider；`MonItems`、`Mongen`、`MapInfo` 等不得送入 NPC 解释器。
7. 冷启动日志中脚本和领域配置 Error/Fatal 为 0，运行期未知指令计数为 0。
8. 完成至少两个保存周期、优雅退出和冷启动；脚本状态、持久变量和领域数据不损坏。
9. 代表性人物、NPC、任务、怪物、物品、行会和定时活动探针通过。
10. 生成版本化兼容报告，能追溯到源 Envir 摘要、代码提交、配置、数据库/资源依赖摘要和测试结果。

### 3.2 外部依赖契约

仅复制 Envir 无法凭空提供数据库中的物品、怪物、地图记录，也无法提供客户端 WIL/PAK、地图文件、登录器变量和第三方服务。最终验收分两级：

| 等级 | 定义 | 可声明内容 |
|---|---|---|
| E1：Envir 语法兼容 | Envir 原样复制；目标已有匹配的物品/怪物/地图和客户端资源 | “脚本与领域配置零未知、可启动” |
| E2：完整版本迁移 | Envir 加版本依赖清单；所需数据库、地图和客户端资源已导入或映射 | “代表性整套玩法可直接运行” |

缺失外部依赖必须在预检阶段报出结构化错误。不能把 E1 宣称成 E2，也不能用空对象或假资源让启动表面成功。

## 4. 架构总览

```text
Envir 原始目录
    │
    ▼
EnvirSourceClassifier
    ├─ NPC / QuestDiary / 系统脚本 ──► PhysicalTextFileProvider
    ├─ MonItems ─────────────────────► MonsterDropProvider
    ├─ MonUseItems / SmartMonster ───► MonsterContentProvider
    ├─ MapInfo / Mongen / MapQuest ──► WorldContentProvider
    ├─ 商店 / 配方 / 列表 ───────────► 对应领域 Provider
    └─ 运行数据 / 客户端依赖 ───────► 依赖清单，不进入脚本解释器
                                            │
                                            ▼
                         CandidateEnvirSnapshot
                         ├─ 脚本注册表
                         ├─ 服务器常量目录
                         ├─ 触发映射
                         ├─ 领域配置快照
                         ├─ 外部依赖清单
                         └─ 诊断与覆盖率
                                            │
                            主线程安全点原子发布
                                            │
                                            ▼
                                  运行期 Resolver / Hook
```

核心不变量：一个候选版本要么整体可用并发布，要么完整保留上一成功版本。不得出现脚本已经换新、爆率仍旧、常量目录失败、地图配置只更新一半的状态。

## 5. 模块设计

### 5.1 `ServerSymbolModule`：服务器常量深模块

外部 seam 只暴露一个解析接口：

```csharp
public interface IServerSymbolResolver
{
    ServerSymbolResult Resolve(
        ServerSymbolContext context,
        ServerSymbolReference reference);
}
```

`ServerSymbolContext` 承载可选的人物、英雄、NPC、地图、物品、怪物、宠物、攻击者、受击者、行会/攻城和触发结果；调用者不需要知道具体常量由哪个 Adapter 实现。

内部 Adapter：

- `PlayerSymbolAdapter`：人物身份、等级、属性、负重、装备、货币；
- `HeroSymbolAdapter`：`H.*` 英雄字段；
- `CombatSymbolAdapter`：攻击目标、伤害、击杀者、受击值、技能；
- `ItemEventSymbolAdapter`：当前使用、拾取、掉落、装备和容器物品；
- `PetSymbolAdapter`：宝宝/宠物坐标、属性、目标和击杀；
- `GuildConquestSymbolAdapter`：行会职位、城堡、攻城计划和费用；
- `ServerClockSymbolAdapter`：服务器名、时间、在线人数、开区时长；
- `ClientMetadataSymbolAdapter`：登录器版本、推广标识和允许公开的客户端信息；
- `VariableExpressionAdapter`：`STR(...)`、动态索引和现有变量模块，只复用不重建存储。

解析结果必须区分：

```text
Resolved          已取得值
ContextUnavailable 常量已支持，但当前事件没有所需对象
DependencyMissing 需要的领域或客户端数据不存在
SensitiveDenied   因隐私/安全拒绝
Unsupported       尚未兼容
InvalidReference  语法或索引非法
Faulted           Adapter 异常
```

禁止把以上状态全部压成空字符串。对话显示可以根据兼容契约把 `ContextUnavailable` 渲染为空或 `0`，但预检和指标仍必须保留结构化状态。

### 5.2 `ServerSymbolCatalog`：名称与契约事实源

每个常量登记：

- 规范名称、大小写不敏感别名和参数形式；
- 返回类型：字符串、整数、小数、日期时间或对象摘要；
- 所需上下文；
- 翎风语义、LyoCrystal 数据来源和兼容等级；
- 无上下文时的确定性结果；
- 是否涉及隐私、服务器路径、机器标识或账户信息；
- 可用脚本入口和触发时点；
- 自动化测试编号、说明书页面、语料次数和最后复核日期。

目录随候选快照只读发布。重复别名、类型冲突或同名不同上下文契约会拒绝候选。

### 5.3 `ScriptTextRenderer`：统一文本替换

当前 `ParseSay` 按空格切分，`ReplaceValue` 使用贪婪正则并只处理有限分支，不适合 50,000 级动态表达式。新模块负责：

- 在一行中解析多个 `<$...>`；
- 支持 `STR(...)`、数组/容器索引和带点命名空间；
- 区分服务端 `<$GAMEGOLD>` 与客户端实时 `$$GAMEGOLD`；
- 保留中文、反斜杠换行、按钮和颜色标记；
- 限制单行长度、占位符数量、嵌套深度和展开后大小；
- 返回渲染结果和诊断，不直接修改玩家对象。

NPC 对话、命令参数、触发上下文显示和 C# `ScriptApi` 必须共用该 seam，随后删除旧的重复替换路径。

### 5.4 `EnvirSourceClassifier`

职责是把每个文件交给唯一模块，不解释业务内容：

| 翎风目录/文件 | 所有者 |
|---|---|
| `Market_Def`、`Npc_def` | NPC Provider |
| `QuestDiary`、`DeFines` | 可调用/定义 Provider |
| `MapQuest_def/QManage`、`QFunction` | 系统 Hook Provider |
| `Robot_def` | 定时/机器人调度 Provider |
| `MonItems` | 爆率 Provider |
| `MonUseItems` | 怪物装备 Provider |
| `SmartMonster` | 怪物行为配置 Provider |
| `MapInfo.txt`、`Mongen.txt`、区域配置 | 世界配置 Provider |
| 商店、配方、列表、排行榜 | 对应领域 Provider |
| `.sav/.prc`、角色/行会/日志等 | 运行数据，禁止随脚本热更覆盖 |
| WIL/PAK、地图和登录器文件 | 外部资源依赖清单 |

输出必须包含 `Accepted`、`RuntimeData`、`ExternalDependency`、`Rejected` 四类计数；任何未分类文件阻断 E1 验收。

### 5.5 `CandidateEnvirSnapshot`

把脚本、常量目录、系统触发、领域 Provider、依赖清单和诊断固定为同一个版本。接口只提供：

```text
Build(source, profile) -> CandidateEnvirSnapshot
Publish(candidate)     -> PublishResult
Current                -> PublishedEnvirSnapshot
```

内部完成编码、引用图、标签、命令、常量、领域 Schema、外部依赖和资源限额验证。发布只能在服务端主线程安全点发生，并能恢复上一快照。

### 5.6 领域 Provider

每个 Provider 必须满足相同发布协议，但保留自己的 Schema：

1. `MonsterDropProvider`：`MonItems` 概率、组、条件和归属；
2. `MonsterContentProvider`：`MonUseItems`、怪物装备和可复用 SmartMonster 字段；
3. `WorldContentProvider`：`MapInfo`、`Mongen`、区域、传送和副本；
4. `RobotScheduleProvider`：启动、周期、固定时刻、重入、预算和停服取消；
5. `CommerceContentProvider`：商店、配方、回收和货币事务；
6. `RuleListProvider`：白名单、黑名单、名字列表和根规则。

不得创建一个“万能 Envir Provider”。不同领域的数据一致性、失败模式和主线程提交要求不同。

### 5.7 `EnvirCompatibilityPreflight`

扩展现有预检，不另造平行扫描系统。输出：

- 源摘要、文件清单和编码分布；
- 文件所有权与未归属项；
- 命令/检测/动作/触发/常量使用清单；
- 动态变量与常量表达式族；
- 引用图、缺失目标和循环；
- 领域 Schema 错误；
- 物品、怪物、地图、客户端资源依赖；
- B/C/D/E/X 分布和阻断项；
- 与上一版本的新增、消失和语义变化。

预检本身只读，不修改源 Envir、数据库或客户端资源。

## 6. 服务器常量实施范围

### 6.1 P0：通用人物与服务器显示

首批直接影响绝大多数 NPC 页面：

- 人物：`USERNAME/USERALLNAME/LEVEL/JOB/GENDER/HP/MAXHP/MP/MAXMP/EXP/MAXEXP/PKPOINT`；
- 基础属性：`AC/MAXAC/MAC/MAXMAC/DC/MAXDC/MC/MAXMC/SC/MAXSC/HIT/SPD/LUCK`；
- 地图：`MAP/MAPTITLE/X/Y/FBMAP/FBMAPNAME`；
- 行会：`GUILDNAME/RANKNAME/GUILDMEMBERCOUNT`；
- 货币：`GOLDCOUNT/GAMEGOLD/GAMEPOINT/GAMEDIAMOND/GAMEGIRD/JADE/GAMEGLORY/CREDITPOINT`；
- 服务器：`DATE/TIME/DATETIME/YEAR/MONTH/DAY/HOUR/MINUTE/SECOND/SERVERNAME/USERCOUNT/ONUSERCOUNT/DUMMYCOUNT`；
- 装备：衣服、武器、头盔、项链、左右戒指、左右手镯、腰带、鞋、护身符、宝石、盾牌和时装槽位。

P0 每项必须确认别名，例如当前项目的 `CLASS/MAPNAME/X_COORD/Y_COORD/CREDIT` 与翎风 `JOB/MAPTITLE/X/Y/CREDITPOINT` 的关系。

### 6.2 P1：事件与玩法上下文

- `KILLMONNAME/KILLMONX/KILLMONY/GETEXP`；
- `CURITEMNAME/CURITEMPOS/USEITEMNAME/PICKDROPITEMNAME`；
- `KILLER/CURRRTARGETNAME/STRUCKHP/PKPOWER/CURRRUSEMAGICID`；
- `SCRIPTPARAM1...N` 和触发参数；
- 组队成员数量与 `TEAM0...TEAMX`；
- 延迟召回剩余时间、活动倍率和剩余时间。

这些常量只能在正确事件生命周期内存在。实现时必须让触发 Adapter 携带不可变事件 DTO，不能从全局“最后一次事件”字段读取，避免串号和重入污染。

### 6.3 P2：英雄、宝宝和宠物

- `H.*` 人物镜像属性、装备、经验、职业、等级、地图和坐标；
- 英雄当前/锁定目标、伤害、击杀和技能；
- `SLAVE*` 与 `Pet.*` 的自身、目标、HP/MP、伤害和击杀；
- 主人名、忠诚、怒气、内功和攻击倍率。

若 LyoCrystal 没有等价英雄/宠物模型，状态应为 E；不得把人物或普通宝宝对象冒充英雄。

### 6.4 P3：行会、攻城和服务器配置

- 城堡名称、占领行会、城主、占领天数、攻城日期/列表/费用；
- 创建行会要求、战争费用和行会排名；
- 游戏网站、论坛、下载地址和游戏币显示名称；
- 最高等级、PK、攻击、魔法和道术人物摘要。

银行账号、电话、QQ 等配置只有在明确的公开运营配置中才允许显示；不得从环境变量、机器注册表或秘密存储自动暴露。

### 6.5 P4：高级系统

- 内功、经络、天地结晶；
- 生肖盒、首饰盒、时装全槽位；
- 战斗力、转生、加星、吸收伤害；
- 登录器推广、客户端内核版本和实时 UI 常量。

这些常量必须绑定真实数据模型和客户端能力。数据字段不存在时保持 E，不在 PlayerObject 上堆无来源字段。

### 6.6 安全与隐私

以下项目必须单独审查：

| 常量 | 默认策略 |
|---|---|
| `USERID` | 只允许游戏内非秘密账号显示名；不得返回认证凭据或内部主键 |
| `MACHINEID` | 默认 X；如确有玩法依赖，只能返回会话化、不可逆、不可跨服关联的标识 |
| `SERVERIP` | 只允许显式配置的公开地址，不返回监听地址或内网地址 |
| `GameDirectory/M2Directory` | 默认 X；不暴露宿主绝对路径 |
| `PHONE/BANKACCOUNT*/QQ` | 只能读取专门的公开运营配置，默认空且记录配置缺失 |
| 密码、密保、邮箱等 | 永久 X，不进入兼容回落 |

## 7. 开发顺序与进度

状态只允许：未开始、进行中、已实施、已验证、阻断。百分比不是完成依据。

| 顺序 | 任务 | 当前状态 | 主要工件 | 退出条件 |
|---:|---|---|---|---|
| 0 | LFENV-00 权威语料与验收契约 | 进行中 | 本规格、版本池、E1/E2 定义 | 项目所有者接受范围和零错误定义 |
| 1 | LFENV-01 语料去重与版本画像 | 已验证 | 53 根摘要、家族聚类、代表样本清单 | 每个根有摘要、编码分布、文件类型、内容哈希和家族归属；5 项目录契约测试中的 2 项画像测试通过 |
| 2 | LFENV-02 服务器常量目录 | 已验证 | `lingfeng-server-symbols.csv`、上下文/安全等级/旧实现映射 | 附件 281 个原始条目全部登记；限定 53 个 `Envir*` 根后，513 个在用符号族逐项重算一致；状态仅为 D/X，无 `?`；5 项目录契约测试全部通过 |
| 3 | LFENV-03 常量解析深模块 | 已验证 | Resolver、Catalog、Context、结果类型 | 不修改领域对象即可解析 P0；异常不泄漏、不串号 |
| 4 | LFENV-04 统一文本渲染 | 已验证 | Renderer、语法/限额/诊断测试 | 多占位符、函数、中文、按钮和嵌套全部通过 |
| 5 | LFENV-05 P0 人物/服务器常量 | 已验证 | Player/Server/Equipment Adapter | P0 清单自动化与真实 NPC 页面全部通过 |
| 6 | LFENV-06 P1 事件常量 | 已验证 | Combat/Item/Trigger Adapter | 53 个 P1 规范名已接入；专项 88/88、Base05 全量 780/780，通过双轴审查且真实链无上下文污染 |
| 7 | LFENV-07 P2 英雄/宠物常量 | 已验证 | Hero/Pet Adapter 或 E 清单 | 有模型的逐项通过，无模型的依赖和迁移策略明确 |
| 8 | LFENV-08 P3/P4 与实时常量 | 已验证 | 行会/攻城/高级系统/客户端契约 | 服务端与客户端值来源一致；敏感项失败关闭 |
| 9 | LFENV-09 Envir 文件分类 | 已验证 | Classifier 与所有权清单 | 代表样本未归属文件为 0，运行数据不会被覆盖 |
| 10 | LFENV-10 系统/机器人调度 | 未开始 | QManage/QFunction/Robot 调度 | 启动、周期、固定时刻、停服和重入预算通过 |
| 11 | LFENV-11 爆率与怪物内容 | 未开始 | MonItems/MonUseItems/SmartMonster Provider | 真实怪物掉落、装备和配置差分通过 |
| 12 | LFENV-12 地图与刷怪 | 未开始 | MapInfo/Mongen/MapQuest Provider | 冷启动、刷怪、区域和地图切换通过 |
| 13 | LFENV-13 商店/配方/列表 | 未开始 | Commerce/RuleList Provider | 事务原子、权限、库存和列表加载通过 |
| 14 | LFENV-14 命令与触发长尾 | 未开始 | 按语料频率成组的 Adapter | 所选样本未知命令/触发为 0，真实链每项至少一测 |
| 15 | LFENV-15 外部依赖清单 | 未开始 | 物品/怪物/地图/客户端资源 manifest | 缺失依赖在启动前阻断，E1/E2 不混淆 |
| 16 | LFENV-16 单版本完整切片 | 未开始 | `01酷明` 与 `封神法宝` 迁移证据 | 原样 Envir E1 通过；一个完整玩法 E2 通过 |
| 17 | LFENV-17 多版本回归 | 未开始 | 代表版本矩阵、差异报告 | 十个家族样本均达到声明等级 |
| 18 | LFENV-18 全语料门禁 | 未开始 | 53 根批量预检结果 | 所有目标根 0 未知、0 未归属；阻断根有明确依赖任务 |
| 19 | LFENV-19 灰度、回滚与说明书 | 未开始 | 候选、备份、回滚、正式说明书 | 真实服保存/重启/回滚与两轴审查通过 |

顺序约束：LFENV-02 至 05 是后续所有玩法的基础；领域 Provider 可以在常量 P0 稳定后并行推进，但 LFENV-16 以前必须汇入同一候选快照。

### 7.1 LFENV-03 实施边界

- 运行时代码位于 `src/Server/Server/Scripting/ServerSymbols/`，调用方只通过 `IServerSymbolResolver.Resolve` 取得结构化结果。
- `ServerSymbolCatalog`、Definition、Binding 和具体 Resolver 均为程序集内部实现；在构建候选时复制为只读快照，规范名、别名、同名契约冲突或必填元数据缺失会拒绝整个候选。
- `ServerSymbolContext` 显式声明本次事件可用的领域上下文，并以只读 Binding 提供值；Resolver 不保存“上一次事件”的对象或值。
- 参数、大小写、空白和 `<$...>` 包装由 `ServerSymbolReference` 统一归一化；别名在进入 Binding 前还原为规范名。
- 安全类别分别记录隐私、服务器路径、机器标识、账户信息和凭据，并与允许策略分离；拒绝策略先于取值，Adapter 异常只返回不含异常正文的 `Faulted`；缺上下文和缺依赖分别返回 `ContextUnavailable`、`DependencyMissing`。
- 本阶段不接管 `NPCSegment.ReplaceValue`，也不把目录中的 D 项升级为 B/C；统一文本接入属于 LFENV-04，人物/服务器真实 Adapter 与 P0 完整清单属于 LFENV-05。

### 7.2 LFENV-04 实施边界

- `IScriptTextRenderer` 是统一渲染 seam；每个 `<$...>` 只通过 `IServerSymbolResolver` 取值，普通按钮、颜色文本和客户端 `$$...` 绑定不进入服务端替换。
- 扫描器支持同一行多个、相邻和嵌套占位符，并对引号内逗号、括号、比较符和反斜杠转义保持参数边界；闭合扫描使用显式栈，不在限额生效前递归遍历不可信输入。
- 默认限制为输入 8,192 字符、64 个占位符、4 层嵌套和展开后 32,768 字符；调用方即使自定义限额也不能超过 1 MiB 输入、4,096 个占位符、16 层嵌套和 4 MiB 输出。
- 语法或限额失败整行原子返回原文；单项缺上下文、缺依赖、敏感拒绝、未支持或 Adapter 故障保留该占位符，并返回不含领域异常正文的结构化诊断。
- 本阶段尚不挂接旧 `NPCSegment.ReplaceValue`；LFENV-05 提供 P0 Player/Server/Equipment Adapter 后，NPC 对话、命令参数、系统触发和 `ScriptApi` 才能在同一真实调用链切换，避免先接入一个只能返回 `DependencyMissing` 的空运行时。

### 7.3 LFENV-05 实施边界

- P0 运行时目录以 6.1 的 82 个规范名为阶段契约；`CLASS/MAPNAME/X_COORD/Y_COORD/CREDIT/ARMOUR` 等旧名称作为别名进入同一 Resolver，非翎风兼容模式继续执行旧 `switch`，不得改变默认配置行为。
- NPC `#SAY` 每行只建立一次人物、地图、行会、装备和服务器时间只读快照；`#IF/#ACT` 参数、系统触发页与 `ScriptApi.ResolveLegacyToken` 复用同一 Renderer。快照构建或 Adapter 异常原子保留原文，只产生不含领域值和异常正文的结构化诊断。
- 当前数据模型没有独立的游戏点、金刚石、灵符、灵玉、荣誉、假人计数、盾牌和时装槽位。显示兼容值分别为 `0` 或“空”，Resolver 返回 `CompatibilitySubstitute`，不得把兼容值冒充真实领域数据，也不得据此实现写操作。
- 未登记常量在 Renderer 中返回 `Unsupported` 并保留原文；真实 NPC 入口必须把状态、规范名和固定诊断写入调试日志，日志不得包含玩家名、常量值、脚本文本或宿主路径。
- `DATE/TIME/DATETIME` 在翎风兼容模式分别使用 `yyyy-MM-dd`、`HH:mm:ss`、`yyyy-MM-dd HH:mm:ss`；非翎风模式的旧 `DATE` 继续使用 `ToShortDateString()`。
- LFENV-05 完成时，P0 在“直接”服务器常量语料中的调用覆盖基线为 `61,816 / 108,296 = 57.08%`。GATE-C0 的 80% 是 LFENV-06 至 LFENV-08 继续实现事件、英雄宠物、行会攻城后统一复算的项目门禁，不是扩大 LFENV-05 领域边界或把既有 `STR()` 变量模块重复计入 P0 的理由。

### 7.4 LFENV-06 实施边界

- P1 运行时目录以 6.2 的事件、物品、战斗目标、脚本参数、队伍、召回与倍率常量为阶段契约；P0 的 82 个规范名继续由独立目录测试锁定，新增条目不得混入 P0 清单。
- 击杀、拾取、使用、掉落与伤害触发在领域入口立即复制为不可变快照；脚本执行只通过线程内显式作用域读取，嵌套调用退出后恢复外层上下文，事件结束后不得保留人物、怪物或物品引用。
- `SCRIPTPARAM1...9` 从当前参数化 NPC 页的参数快照读取，并可与同一调用栈中的事件快照组合；缺少的参数保留原占位符。`TEAM0...9` 按当前 `GroupMembers` 稳定顺序读取，越界保留原文。
- `CURRRUSEMAGICID` 在伤害链中读取当前延迟攻击携带的技能枚举值，普通攻击为 `0`；未携带技能标识的其他旧伤害入口按普通攻击处理，不保存“最后一次技能”全局字段。
- 经验与爆率倍数从人物当前百分比属性折算为完整整数倍，剩余时间从对应 Buff 的只读倒计时读取；延迟召回从当前人物尚未到期的五参数 NPC 召回动作读取并向上取整到秒。
- 当前模型没有独立的“攻击人物倍数”和“攻击怪物倍数”可写状态，`POWERRATE/ATTACKMONPOWERRATE` 及其时间返回 `1/0` 兼容基线并产生 `CompatibilitySubstitute`，不得据此实现写操作。`M2DROPITEM` 是聚合后置事件，多物品同时掉落时当前物品名取结果中的首件，只有金币时显示“金币”。

### 7.5 LFENV-07 实施边界

- P2 运行时目录只登记当前项目有独立领域来源的 68 个规范名。英雄持久身份、等级、经验、职业、性别和装备来自 `HeroInfo`；HP/MP、属性、地图、坐标和目标只在已召唤 `HeroObject` 存在时解析，不使用人物值填充未召唤英雄的运行时状态。
- 英雄伤害和击杀在真实领域入口记录本次实际伤害来源，经验归属逻辑保持原状；不可变事件 DTO 以 `Player/Hero/Pet` 身份区分同一主人下的攻击，人物事件不得污染 `H.*`，事件结束后不保留领域对象引用。
- 宝宝从 `PlayerObject.Pets` 当前存活集合按现有集合顺序选择首个对象；`SLAVECOUNT` 统计存活对象，多宝宝选择确定且跳过死亡对象。`PET.CURTARGETFULLNAME` 使用怪物配置全名，`PET.CURTARGETNAME` 使用游戏显示名；无存活宝宝或无存活目标时，对象常量保留原占位符。
- 当前项目没有英雄转生、忠诚、怒气、内功、独立攻人/攻怪倍率、战斗力、加星、吸收伤害字段，也没有宠物 MP 或宠物逐次伤害事件快照；对应项保持 E，依赖分别为新的英雄成长领域模型或宠物伤害事件 seam。宠物击杀名称已通过真实最终伤害来源接入。英雄当前物品、过期物品、改名和目标主人也缺少可证明的事件上下文，保持 E。P4 时装、生肖盒、首饰盒由 LFENV-08 继续判定，本阶段不提前伪造。
- LFENV-07 目录更新后，“直接”服务器常量真实语料的 B/C 覆盖为 `88,896 / 108,296 = 82.09%`，首次达到 GATE-C0 的 80% 覆盖门槛；E/X 调用不计入兼容覆盖，也不以空值吞掉。

### 7.6 LFENV-08 实施边界

- P3 运行时目录首批只登记当前项目有等价只读来源的 9 个规范名：`CASTLENAME/CASTLEGOLD/OWNERGUILD/CASTLEWARDATE/LISTOFWAR/GUILDMASTER1/GUILDMASTER2/GUILDWARFEE/REQUESTBUILDGUILDITEM`。城堡上下文优先取当前 NPC 绑定的 `ConquestObject`，其次取人物所属行会城堡，最后按城堡索引稳定选择服务器默认城堡；不得因来访人物不属于城主行会而隐藏全局城堡拥有者。
- `GUILDMASTER1/2` 只读取当前人物行会的会长职级成员，未入行会时保留占位符；`GUILDWARFEE` 和创建行会所需物品只读取公开服务器配置。费用和物品常量不得读取账户、背包或秘密配置，也不得执行扣费。
- `CASTLEWARDATE` 当前只能返回本进程已知的最近战争开始时间，项目没有持久化翎风攻城申请日期与上次占领日期；`LISTOFWAR` 只能按城堡索引列出已申请或进行中的城堡名称，缺少翎风原生排版和逐次申请日期。两项均以 C 和 `CompatibilitySubstitute` 返回，不冒充完整兼容。
- `CASTLEGETDAYS/CASTLECHANGEDATE/CASTLEWARLASTDATE/REQUESTCASTLEWARDAY/BUILDGUILDFEE/CASTLEDOORSTATE` 缺少等价持久字段或已确认显示契约，保持 E。官网、论坛、客户端下载地址和币种显示名没有明确公开运营配置，高等级/PK/攻击/魔法/道术摘要没有等价的持久排行榜字段，也保持 E；银行账号、电话、QQ、机器路径等敏感项继续 X，禁止从环境变量、注册表或秘密存储推导。
- LFENV-08 首批目录更新后，“直接”服务器常量真实语料的 B/C 覆盖为 `89,169 / 108,296 = 82.34%`。覆盖门槛已满足，但后续整服验收仍以未知常量、未知命令和真实玩法探针为零为准，不能用覆盖率替代逐项兼容。

### 7.7 LFENV-09 实施边界

- `LingFengEnvirFileClassifier` 是翎风 Envir 文件唯一归属 seam；分类优先级固定为路径安全、备份归档、运行数据、可执行工件、文档附件、客户端契约、脚本、领域配置和未归属阻断，同一文件只返回一个所有者与规则 ID。
- 只有所有者为 `Script` 的文件可以进入 `PhysicalTextFileProvider`。`UserData/Market_Saved/Market_Storage/Market_SellOff` 和 `prc/sav/dat/sell/gold/db` 等运行数据始终只保留、不覆盖；备份、文档、客户端契约与领域配置等待各自阶段处理，不得被 TXT 热更新接管。
- `Market_Def/Npc_def/QuestDiary/DeFines` 的合法 TXT、根级或 `Market_Def` 下的 `QFunction-0`，以及 `QManage/RobotManage` 系统入口映射为脚本逻辑 Key；脚本命名空间内不能形成合法 Key 的文件和未知扩展名均拒绝整个候选，不静默降级为普通配置。
- `QFunction-0` 同时存在于根目录和 `Market_Def` 时属于已知目录别名冲突，确定性选择 `Market_Def` 标准入口；仅存在根级文件时作为同一系统入口回退。其他重复逻辑 Key 继续拒绝候选，不扩大通用覆盖规则。
- 规则事实清单为 `Docs/generated/scripting/lingfeng-envir-file-ownership.csv`。24 个版本家族代表样本逐文件扫描时，隐藏、系统和重解析点沿用画像排除边界；其余文件必须全部唯一归属，未归属为零。

## 8. 测试设计

### 8.1 单元测试

- 名称归一化、别名、参数、大小写和空白；
- 一个文本中的多个常量、相邻常量和嵌套表达式；
- 类型格式、日期格式和文化无关数值；
- 缺上下文、依赖缺失、敏感拒绝和 Adapter 异常；
- 限额：单行、常量数、嵌套深度、展开大小；
- 动态索引、数组范围和非法成员；
- 每个 Catalog 条目必须关联至少一个测试编号。

### 8.2 契约测试

同一常量在 NPC 对话、命令参数、系统触发和 C# ScriptApi 中必须通过同一 Resolver 得到相同结果。测试只跨 `IServerSymbolResolver` 和 `ScriptTextRenderer` seam，不直接调用内部 Adapter 私有方法。

### 8.3 领域集成测试

| 场景 | 必须断言 |
|---|---|
| 人物登录 | 身份/行会/服务器常量正确，C#/TXT 不重复 |
| NPC 对话 | 中文、按钮、多常量和装备槽位正确 |
| 击杀怪物 | 人物与英雄归属、怪物名、坐标和经验不串号 |
| 物品使用/拾取 | 当前物品上下文只在事件内有效 |
| 战斗前后 | 目标、技能、伤害和取消结果时点正确 |
| 宝宝/宠物 | 多宝宝选择规则可重复且不会取到已死亡对象 |
| 行会/攻城 | 无行会、非城主、非攻城期等失败路径安全 |
| 定时机器人 | 周期、重入、预算、停服取消和热更新版本正确 |
| 爆率/刷怪 | Provider 快照与领域对象在同一版本可见 |

### 8.4 翎风差分测试

对可启动的参考版本使用固定数据夹具，在翎风和 LyoCrystal 分别记录：

- 渲染文本；
- 常量类型和值；
- 触发次数与顺序；
- 人物、物品、货币和任务前后状态；
- 怪物生成、死亡和掉落；
- 保存、重登和重启结果。

差分结果必须去除时间戳、随机种子等明确非确定字段。无法黑盒验证的项目保持 C/D/E，不因“看起来合理”升级为 B。

### 8.5 语料测试

每个代表 Envir 建立不可改写的输入摘要。测试过程复制到临时隔离目录，只对副本运行：

1. 文件分类；
2. 编码与引用预检；
3. 常量和命令覆盖；
4. 领域 Schema；
5. 外部依赖；
6. 快照构建；
7. 冷启动；
8. 代表玩法探针；
9. 保存、退出和重启；
10. 源目录摘要复核。

### 8.6 性能与稳定性

- 10,000 行 NPC 文本渲染基准；
- 10 万常量解析不产生无界缓存；
- 6,699 文件大 Envir 冷预检与增量热更；
- 机器人和触发风暴下主线程 P95/P99；
- 解析失败、热更失败、停服竞争和重入；
- 指标采用有界样本，不记录账户、机器码或完整脚本文本。

## 9. 阶段门禁

### GATE-C0：常量事实源

- 附件和代表语料全部入表；
- 每项都有上下文、类型、数据来源、安全等级和状态；
- 未知 `?` 为 0；
- P0 使用次数覆盖语料常量调用的至少 80%。

### GATE-C1：常量运行时

- P0 全部通过 seam 契约测试；
- 旧 47 个 `switch` 行为无回归；
- 未支持名称不再静默保留原文本；
- 敏感值没有进入日志或客户端。

### GATE-E0：文件所有权

- 代表版本每个文件唯一归属；
- 未归属为 0；
- 运行数据和客户端资源不会被脚本热更新覆盖。

### GATE-E1：脚本与领域配置

- 未知命令、触发、常量、标签、引用为 0；
- 所有领域 Provider 严格解析；
- 外部依赖清单完整；
- 原样 Envir 冷启动无脚本/配置错误。

### GATE-E2：完整玩法

- 至少一个法宝/符文/装备收集类完整玩法通过；
- 登录、NPC、任务、怪物、奖励、持久化和重启形成闭环；
- PC 与 Android 共享协议无差异；
- 经济资产前后账务可核。

### GATE-ALL：多版本目标

- 代表家族全部通过声明等级；
- 53 个 Envir 根都有最新预检结果；
- 不能通过的根必须有明确阻断依赖，不能写成完成；
- 全量 Base05、专项、说明书严格构建、发布物烟测和双轴审查全绿。

## 10. 失败策略

- 未知服务器常量：候选预检失败，列出文件、行号、上下文和建议任务。
- 已支持但缺上下文：按单项契约输出兼容值，并记录限频诊断；不得读“上一次事件”。
- Adapter 异常：当前解析失败关闭，不回落到另一套可能产生副作用的实现。
- 领域文件错误：整次候选不发布，上一完整快照继续服务。
- 外部依赖缺失：启动前阻断 E2；不自动生成假物品、假怪物或假地图。
- 热更新期间停服：取消候选构建，禁止在主线程安全点之后留下半发布状态。

## 11. 安全与线程

- 人物、物品、怪物、地图、行会和经济状态只在服务端主线程修改。
- 服务器常量 Resolver 原则上只读；需要延迟查询或外部服务的内容不得伪装成同步常量。
- 禁止暴露密码、密保、邮箱、宿主绝对路径、数据库连接和可跨服追踪的机器标识。
- 文件读取限制在配置根内，拒绝重解析点、路径穿越和超限文件。
- HTTP、数据库写入、启动外部程序和客户端任意窗口继续受独立 Kill Switch 控制。
- 参考服语料只读；测试在隔离副本和隔离数据库进行。

## 12. 发布与回滚

每次候选必须包含：

- 源 Envir manifest 与摘要；
- 常量目录版本；
- 脚本和领域快照摘要；
- 外部依赖清单；
- 兼容状态和已知差异；
- 测试结果与发布物摘要；
- Setup 配置片段；
- 精确回滚片段和数据库/资源恢复边界。

灰度顺序：只读预检 → 隔离冷启动 → 单个 NPC → 单个系统触发 → 一个完整玩法 → 单地图/小流量 → 完整保存周期 → 冷启动 → 扩大版本范围。

回滚必须同时恢复脚本、常量目录、领域 Provider 和配置开关；只关闭 TXT 而遗留新的爆率、刷怪或全局回落策略不算完整回滚。

## 13. 文档与工件

计划工件：

- `Docs/generated/scripting/lingfeng-server-symbols.csv`：服务器常量唯一事实清单；
- `Docs/generated/scripting/lingfeng-envir-roots.csv`：版本根、摘要和家族；
- `Docs/generated/scripting/lingfeng-envir-file-ownership.csv`：文件所有权与依赖；
- 说明书“服务器常量”章节；
- 每个领域 Provider 的 Schema 和迁移页；
- 单版本与多版本 Evidence；
- E1/E2 运维 Runbook。

生成清单由测试重新计算并比对，不能只提交人工填写的静态数字。Evidence 只保存真实执行结果，不承担动态进度事实源。

## 14. 最终完成定义

只有同时满足以下条件，才能声明“目标翎风 Envir 可零报错直接运行”：

1. 服务器常量清单无未知项，目标语料所有已用常量均为 B 或已接受的 C；
2. 未知命令、检测、动作、触发和引用为 0；
3. Envir 文件未归属为 0，且没有静默跳过；
4. 领域 Provider 与脚本以同一候选版本原子发布；
5. E1 外部依赖契约全部满足；声明 E2 时资源和数据库依赖也全部满足；
6. 原样 Envir 通过严格预检、冷启动、两个保存周期、优雅退出和重启；
7. 至少一个整套玩法完成登录、NPC、任务、怪物、奖励、持久化和客户端显示闭环；
8. 代表版本矩阵和目标 Envir 根均有新鲜证据；
9. 安全、隐私、主线程、预算和回滚门禁通过；
10. 全量自动化、发布物烟测、说明书构建和双轴审查全部通过。

任一目标版本仍有 D/E/X 实际使用项、外部依赖缺失或只能靠忽略错误启动时，该版本不得标记完成。
