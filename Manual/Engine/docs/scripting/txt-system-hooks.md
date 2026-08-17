# TXT 系统入口与基础触发

- 兼容等级：B（生命周期与伤害前置）/ C（战斗后置、拾取、击杀与掉落安全子集）
- 适用入口：`SystemScripts/QManage`、`SystemScripts/QFunction-0`
- 最后复核：LFM2-2026-08-15-snapshot

## 人物升级

在受控内容根创建或迁移 `SystemScripts/QFunction-0.txt`：

```text
[@PLAYLEVELUP]
#ACT
GIVEGOLD 100
#SAY
升级奖励已发放。
```

服务端人物升级事件先走现有 C# `OnPlayerLevelUp` Hook。C# Handler 返回“已处理”时不会再次执行 TXT；C# 未处理、物理 TXT 已启用、选择 `LFM2-` 兼容版本且当前成功快照包含精确标签时，才派发 TXT 页面。执行沿用 NPC 主线程和即时跳转预算，但系统触发不发送 NPC 对话窗口。

## 人物登录

在受控内容根创建或迁移 `SystemScripts/QManage.txt`：

```text
[@LOGIN]
#ACT
MOV P0 1
```

服务端登录事件遵循相同的单来源规则：先执行 C# `OnPlayerLogin` Hook，仅在 C# 未处理时派发精确的 `[@LOGIN]`。TXT 页面沿用登录玩家上下文，可以设置个人变量，但不会打开 NPC 对话框。

`TxtScriptsEnabled` 只控制物理 TXT 来源，`CSharpScriptsFallbackToTxt` 独立控制 C# 运行时启用后的执行回落。两套运行时共存且需要执行这些 TXT 系统页时，必须显式设置 `CSharpScriptsFallbackToTxt=true`；设为 `false` 时即使没有对应 C# Handler 也禁止回落。C# Handler 抛异常属于失败关闭，生命周期入口和后置事件都不会继续执行 TXT，以免产生重复或一半副作用。

## 系统脚本来源

翎风布局的 `MapQuest_def/QManage.txt` 与 `Robot_def/ROBOTMANAGE.TXT` 已分别映射到 `SystemScripts/QManage` 和 `SystemScripts/RobotManage`；`QFunction-0` 使用 `SystemScripts/QFunction-0`。这些文件仍属于统一候选快照，编码、引用、严格命令、变量声明或标签验证失败时整批回滚。

`QuestDiary` 与 `DeFines` 下的 `.ini` 脚本文件按与 `.txt` 相同的页面脚本处理，逻辑键会去掉扩展名。普通 `GOTO` 优先匹配当前文件页面；当前文件没有目标且整个候选恰好只有一个同名页面时，允许跳入该唯一页面。零个或多个候选都不会猜测。酷明遗留的 `@_@...` 外部回调页与已识别的机器人外部页会登记为 E2 页面依赖，不伪造成已执行页面；除已确认的单个历史重复页按首个页面语义归一外，其他重复标签仍拒绝候选。

## 定时机器人

`Robot_def/AUTORUNROBOT.TXT` 映射到 `SystemScripts/AutoRunRobot`，示例：

```text
#AutoRun NPC SEC 5 @五秒任务
#AutoRun NPC MIN 10 @十分钟任务
#AutoRun NPC HOUR 2 @两小时任务
#AutoRun NPC RUNONDAY 20:30 @每日任务
#AutoRun NPC RUNONWEEK 5:19:55 @每周五任务
```

对应页面必须写在 `Robot_def/ROBOTMANAGE.TXT` 中，例如 `[@五秒任务]`。标签大小写不敏感并优先完整一致；兼容酷明旧版本时，可去除确定的 `Mir2_`、前导编号和 `Rm` 装饰，但去除后必须恰好命中唯一同义页。没有候选或存在歧义都会拒绝整个候选并保留上一成功版本，不按包含关系、相似度或页面顺序猜测。`RUNONDAY` 接受 `HH:mm` 或 `HH:mm:ss`，`RUNONWEEK` 使用 `0=星期日` 至 `6=星期六` 的 `日:时:分[:秒]` 格式。

周期任务在发布成功后开始计时，固定时刻任务按服务器本地时间运行。单次主循环最多执行 128 个到期页，递归进入会被拒绝；一个页面异常不阻断同 tick 的其他页面。任何候选语法或编码错误都会拒绝整次热更新并继续使用上一成功版本；服务停止后全部调度立即清空。

## 战斗前置与取消

`@ATTACKDAMAGE` 在玩家造成物理伤害的计算前执行，`@STRUCKDAMAGE` 在玩家受到物理伤害的计算前执行。二者共享当前 `PlayerDamageRequest`，仅允许通过 `CHANGEDAMAGEVALUE` 修改伤害字段（0）或防御字段（1）：

```text
[@ATTACKDAMAGE]
#ACT
CHANGEDAMAGEVALUE 0 = 0
```

运算符支持 `=`、`+`、`-`、`*`、`/`、`%`。修改后最终伤害小于等于 0 时，请求会被显式标记为取消。命令离开伤害前置上下文后失败关闭，不会修改其他事件。

`@ATTACK` 与 `@STRUCK` 在物理伤害结算后执行，接收不可变的 `PlayerDamageResult`；TXT 动作不能反向篡改已经结算的伤害。魔法攻击标签尚未开放。

## 物品与怪物后置触发

- `@PICKUPITEMEX`：仅在玩家自身成功拾取物品或金币后执行；拾取权限或容量校验失败时不触发。
- `@KILLMON`：怪物死亡后按 `EXPOwner` 解析玩家或英雄主人并执行；没有玩家归属时不触发。
- `@M2DROPITEM`：一个怪物掉落批次至少生成一项物品或金币后执行一次，结果只读；空结果不触发，当前不兼容翎风逐物品改名、改色语义。

这些事件均先查同一 `ScriptRegistry` 接缝。精确 C# Handler 已注册时，即使 Handler 选择继续，也不会再回落 TXT；异常同样失败关闭，避免奖励或副作用重复执行。执行仍受主线程、重入和 TXT 即时跳转预算约束。

系统触发只允许同一系统脚本内的即时 `GOTO`，且每次触发共用一个有限预算。`DELAYGOTO`、`GOTOLABEL`、`CALL`、`TIMERECALL`、`TIMERECALLGROUP`、`BREAKTIMERECALL` 和 `GROUPGOTO` 在系统触发上下文中会以 `TXT-RUNTIME-002` 阻断，避免把导航逃逸到后续 tick、其他脚本或其他玩家。系统页的 `#SAY` 使用独立丢弃缓冲，不会污染玩家正在进行的 NPC 对话。

## LFENV-14 魔法与人物死亡触发

- `@MAGICATTACK` 与 `@MAGICSTRUCK` 在真实伤害后置时点执行，只有本次实际生效技能 ID 非零才触发；普通攻击仍只执行 `@ATTACK/@STRUCK`。
- `@PLAYDIE` 以死亡人物为脚本人物，`<$KILLER>` 来自最终 `LastHitter` 快照；`@KILLPLAY` 以可归属的击杀人物为脚本人物。英雄及人物/英雄宝宝归属到主人，没有人物归属时不执行 `@KILLPLAY`。
- C# 人物死亡 Handler 已注册时抑制同一 QFunction TXT 触发，即使 Handler 返回未处理也不重复发奖。QFunction `@PLAYDIE/@KILLPLAY` 不替代旧 DefaultNPC `[@_Die]`，两套页面按各自语义执行。
- 高频检测支持 `EQUAL 左值 右值`、`LARGE 左值 右值`、`SMALL 左值 右值`，以及放在任一单检测前的 `NOT` 或 `!`。原版语料中的单参数 `EQUAL 值` 按与空字符串比较处理，用于检测未传参或未赋值变量。大于/小于只接受十进制数，错误参数失败关闭。
- `GMEXECUTE` 不是任意管理命令入口。兼容层只允许 `探测 人物名` 与 `开始提问 @页面`：前者只读在线人物的真实地图坐标，后者把 `SystemScripts/QManage` 中的精确页面逐一派发给当前在线人物。页面缺失或参数超出白名单时失败关闭；`开始提问` 同时登记对应的 E2 系统页依赖。

## 明确未开放

- 英雄专用、地图区域、计时器和社会系统触发只有在事件时点、取消能力、上下文、异常、重入和耗时预算全部验证后才能升级状态。
- 不允许同时由 C# 与 TXT 执行同一个升级奖励；迁移期间以 C# Handler 的“已处理”结果作为去重边界。

严格模式仍会以 `TXT-SNAPSHOT-016` 阻止上下文不完整的标签，包括 `@KILLSLAVE`、`@GROUPKILLMON`、`@PICKUPITEM`、`@DROPITEM`、`@HUMDROPITEM` 和 `@ITEMEXPIRED`。不能用相近事件冒充这些标签。

## 排错

- 标签必须精确写为 `[@LOGIN]` 或 `[@PLAYLEVELUP]`，不进行模糊匹配。
- 兼容版本为空或 `TxtScriptsEnabled=false` 时不派发。
- 热重载失败继续使用上一成功 `TextFileProvider`，因此触发不会读取半发布文件。
