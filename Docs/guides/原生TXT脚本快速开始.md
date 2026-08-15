# 原生 TXT 脚本快速开始

- 状态：TXT-01 至 TXT-15 已实施；2026-08-16 已完成真实服务器备份、灰度部署、完整保存周期、冷启动复验，以及 Android 模拟器进服和灰度 NPC `[@MAIN]`/`[@VERIFY]` 实交互闭环
- 负责人：项目所有者
- 事实源：运行行为以 `src/Server/Server/`、`Configs/LingFengTxtPilot/` 与自动化测试为准；范围和门禁以 `Docs/design/scripting/翎风TXT脚本兼容迁移实施规格.md` 为准
- 取代关系：无
- 适用范围：物理 TXT 来源、编码、目录布局、组合来源、快照重载、基础语法、控制流、统一变量模块，以及高频人物、物品、货币、地图、怪物、宝宝、任务和行会命令
- 最后复核日期：2026-08-15

## 当前边界

当前可以在不生成 `.cs` 包装文件的情况下，把原生 TXT NPC 送入既有 `NPCScript` 解析器。C# 与 TXT 可以分别开启或同时开启；同一逻辑 Key 只发布一个来源，并记录采用来源和被遮蔽来源。默认 `CSharpFirst` 保持既有行为，也可显式改为 `TxtFirst`。物理文件修改会经过独立 watcher 防抖并构建完整候选快照；编码、路径、大小、重复页签、缺失跳转/包含目标、循环包含或语法分词失败时保留上一成功版本。命令级全量严格校验仍要随后续命令包逐项完成，不应将当前阶段宣称为完整翎风兼容。

`Variables` 目录中的 `VAR` 声明会与 C# 声明合并后原子发布；冲突或非法默认值会保留上一成功快照。固定编号、命名整数/小数、`MOV/INC/DEC/MUL/DIV/CALC/FORMULATION/CHANCE` 以及 `L$`、`D$` 都复用统一变量模块。变量的作用域、持久化和边界见网页说明书的“变量系统”。

## 1. 准备受控内容根

`LyoCrystal` 布局只读取以下白名单目录中的 `.txt` 文件：

- `NPCs`：普通 NPC；
- `QuestDiary`：被 `#INSERT/#INCLUDE` 调用的脚本库；
- `SystemScripts`：系统入口；
- `Defines`：公共定义或片段；
- `Variables`：变量声明文本。

其他扩展名、根目录文件以及 `Logs` 等非白名单目录不会进入脚本快照。符号链接、重解析点、根目录逃逸和超过上限的文件会使候选快照拒绝发布。

## 2. 新建无副作用 NPC

在 `Envir/NPCs/原生TXT示范.txt` 中保存：

```text
[@MAIN]
#SAY
原生 TXT 已加载。
#INCLUDE [QuestDiary/示范/公共对话.txt] @公共
```

在 `Envir/QuestDiary/示范/公共对话.txt` 中保存：

```text
[@公共]
{
#SAY
跨目录包含已加载。
}
```

文件可使用 UTF-8 BOM、严格 UTF-8 或 CP936/GBK。自动判定顺序为 UTF-8 BOM、严格 UTF-8、严格 CP936；带 BOM 但正文无效的文件不会回退为 CP936。CRLF、LF 和 CR 会被识别并保存在来源元数据中。

## 3. 修改配置

在 `Configs/Setup.ini` 中配置：

```ini
[CSharpScripts]
CSharpScriptsEnabled=false

[TxtScripts]
TxtScriptsEnabled=true
TxtScriptsPath=Envir
TxtScriptsLayout=LyoCrystal
TxtScriptsMaxFileBytes=1048576
TxtScriptsSourcePriority=CSharpFirst
TxtScriptsHotReloadEnabled=true
TxtScriptsDebounceMs=500
TxtScriptsMaxImmediateTransitions=64
TxtScriptsCompatibilityVersion=LFM2-2026-08-15-snapshot
TxtScriptsStrictCompatibility=true
```

`TxtScriptsEnabled` 默认是 `false`，单文件上限默认是 1 MiB。`TxtScriptsSourcePriority` 可取 `CSharpFirst` 或 `TxtFirst`；生产默认前者，避免仅启用 TXT 功能就改变已有 C# 文本。发生同 Key 冲突时日志同时给出 Key、选中来源和被遮蔽来源。路径不存在、编码损坏、重复逻辑 Key 或安全检查失败时，服务端启动应失败关闭，并在错误中给出源文件路径。

`TxtScriptsHotReloadEnabled` 默认是 `true`，但只有在 `TxtScriptsEnabled=true` 时生效；默认防抖为 500ms。启用 C# 推送模式时，本地 TXT watcher 自动禁用。每次成功发布会记录版本、SHA-256 摘要、变更 Key、加载耗时和成功时间；失败会记录错误数并继续服务上一版本。

`TxtScriptsMaxImmediateTransitions` 限制一次对话链中的即时 `GOTO/CALL/GOTOLABEL`。默认 64 步；超限后服务端移除待执行的即时跳转、结束当前 NPC 对话，并记录 `TXT-RUNTIME-001`。`#INSERT/#INCLUDE` 最大引用深度为 16，循环或超深候选快照不会发布。

`TxtScriptsCompatibilityVersion` 为空时继续使用原有 Crystal 命令参数语义；选择已审计的 `LFM2-...` 版本后才启用翎风扩展参数。`TxtScriptsStrictCompatibility` 默认开启，后续命令包会按对应版本清单逐批把未知或未发布命令拦在候选快照阶段。

## 4. 使用翎风目录布局

若内容根保持翎风 `Mir200/Envir` 结构，把布局改为 `LingFeng`。当前映射如下：

| 翎风路径 | LyoCrystal 逻辑 Key |
|---|---|
| `Market_Def/比奇/老兵.txt` | `NPCs/比奇/老兵` |
| `Npc_def/比奇/老兵.txt` | `NpcDefs/比奇/老兵` |
| `QuestDiary/任务/主线.txt` | `QuestDiary/任务/主线` |
| `MapQuest_def/QManage.txt` | `SystemScripts/QManage` |
| `Robot_def/ROBOTMANAGE.TXT` | `SystemScripts/RobotManage` |
| `DeFines/公共/变量.txt` | `Defines/公共/变量` |

`Market_Def` 与 `Npc_def` 即使文件名相同也使用不同前缀，不会互相覆盖。`MonItems`、`Mapinfo.txt`、`Mongen.txt` 等领域配置不会交给 NPC 文本解析器。

## 5. 验证与回退

启动后应出现“已加载 N 个物理 TXT 文本”的日志；与示范 NPC 对话时应同时看到主文件和包含文件中的中文。若加载失败，优先检查错误中的完整路径、编码声明、文件大小和重复 Key。

回退单个来源优先级时先恢复 `TxtScriptsSourcePriority=CSharpFirst`；全局回退时设置 `TxtScriptsEnabled=false`，保留 `CSharpScriptsEnabled=true`，然后重启服务端。关闭物理来源时不会访问 `TxtScriptsPath`，现有 C# 默认行为保持不变。
