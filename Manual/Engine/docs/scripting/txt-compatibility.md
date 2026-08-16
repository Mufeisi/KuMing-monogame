# 原生 TXT 与控制流

!!! warning "兼容范围"
    本页只声明 TXT-01 至 TXT-06 已用测试锁定的行为。通用业务命令包、系统触发、富文本 UI 和高风险外部能力仍需按后续门禁完成，不属于本页的兼容承诺。

## 加载与热重载

物理 TXT 默认关闭。启用后，受控目录中的 UTF-8 BOM、严格 UTF-8 或 CP936 文件会进入与 C# 文本定义共用的 Provider。同 Key 冲突由 `TxtScriptsSourcePriority` 决定，默认 `CSharpFirst`。

热重载先构建完整候选快照，再校验编码、路径、页签、引用图和可分词性。任一错误都保留旧快照；成功时记录版本、摘要、变更 Key 和耗时。

## 基础语法

- 结构行忽略前导空白并不区分大小写，`#SAY/#ELSESAY` 正文保留缩进。
- 参数支持双引号、空字符串、中文、`\"`、`\\`、`\n`、`\r` 和 `\t`。
- 行尾奇数个反斜杠表示显式续行；诊断仍指向该逻辑行的起始原始行。
- 已锁定 `#IF/#ACT/#SAY/#ELSEACT/#ELSESAY` 段落边界。

## 控制流

`GOTO`、`DELAYGOTO`、`BREAK`、`CALL` 和 `GOTOLABEL` 使用既有 NPC 执行缝。`CALL` 既可调用 `NPCs` 中的短名，也可使用 `QuestDiary/目录/脚本.txt` 跨目录逻辑 Key。`GOTOLABEL` 支持 0 至 8 模式的组队、行会、地图和坐标范围选择；模式 8 可在选中在线目标后通过统一变量模块传递一对源变量和接收变量，目标写入失败时不会排入跳转。

发布前会拒绝缺失脚本、缺失页签、`#INSERT/#INCLUDE` 循环以及超过 16 层的包含链。运行时单次对话默认最多执行 64 个即时跳转；超限记录 `TXT-RUNTIME-001` 并安全结束对话。

## 变量声明与命令

物理 TXT 中的 `VAR` 声明会与 C# 注册声明合并后一次发布。类型、作用域或默认契约冲突会拒绝整次候选快照，旧脚本、旧声明和已有变量值继续服务。`MOV/INC/DEC/MUL/DIV/CALC/FORMULATION/CHANCE`、`L$` 与 `D$` 均进入现有 `ScriptVariableModule`，不会建立第二套 TXT 变量存储。完整语法和生命周期从[变量系统](variables.md)进入。

## 配置

```ini
[TxtScripts]
TxtScriptsEnabled=false
TxtScriptsSourcePriority=CSharpFirst
TxtScriptsHotReloadEnabled=true
TxtScriptsDebounceMs=500
TxtScriptsMaxImmediateTransitions=64
TxtScriptsCompatibilityVersion=
TxtScriptsStrictCompatibility=true

[ScriptMetrics]
Enabled=false
AutoDumpSeconds=0
MaxKeys=2000
```

生产回退时先将 `TxtScriptsSourcePriority` 恢复为 `CSharpFirst`；需要停用物理来源时将 `TxtScriptsEnabled` 设为 `false` 并重启服务端。

## 灰度性能观测

灰度期间启用 `[ScriptMetrics] Enabled=true` 后，服务端会按脚本 Hook、NPC 页面和 NPC 动作采集运行耗时。`AutoDumpSeconds` 大于零时，快照周期性原子写入 `Logs/Scripts/runtime-metrics-latest.json`；也可使用既有管理入口手动导出。

每个条目同时报告全期调用数、总耗时、平均耗时、全期最大耗时，以及最近 2,048 次调用的 `p95Milliseconds`、`p99Milliseconds` 和 `recentSampleCount`。百分位使用最近样本窗口，避免服务端长期运行后旧流量永久掩盖当前版本变化；全期最大值仍用于发现历史尖峰。关闭指标后不记录样本，默认配置无额外运行时开销。

正式灰度至少保存启用前后的同一业务时段快照，并同时核对服务端主循环延迟。P95/P99 只是定位线索，不能代替经济副作用、重复执行和持久化一致性验收。
## 翎风怪物领域文件

在 `TxtScriptsLayout=LingFeng` 下，怪物领域文件与 NPC 脚本分开处理：

- `MonItems`：怪物爆率表，支持物品、金币、分组、跨页调用和变量条件；类型 7 条件命中后调用既有 `QFunction-0` 系统页；
- `MonUseItems`：怪物装备、掉装开关和技能元数据；装备基础属性会参与真实怪物属性计算；
- `SmartMonster`：严格校验的配置快照。客户端动作、声音和旧寻路参数不会在服务端冒充 AI 执行。

这些文件与物理 TXT 文本属于同一候选。语法、引用、重复 Key 或物品依赖错误会拒绝整次发布，运行中的旧怪物内容和掉落表保持不变。成功热更后，已存活怪物在下一次处理或掉落前同步切换属性与掉落快照。C# 与 TXT 同时启用时，掉落来源遵循 `TxtScriptsSourcePriority` 和 `CSharpScriptsFallbackToTxt`。
