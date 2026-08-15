# 人物、物品与金币命令

- 兼容等级：B
- 适用入口：NPC 的 `#IF` 与 `#ACT`
- 最后复核：LFM2-2026-08-15-snapshot

## 人物检测

```text
#IF
CHECKLEVEL >= 40
CHECKJOB 战士
CHECKGENDER 男性
CHECKPKPOINTEX < 100
CHECKEXP >= 1000 < 2000
CHECKHP > 0 <= 500
CHECKMP >= 25
```

`CHECKLEVEL` 是现有 `LEVEL` 的别名，`CHECKJOB` 是 `CHECKCLASS` 的别名。经验、HP 和 MP 支持一组或两组“操作符 数值”，两组条件必须同时成立；可用操作符为 `= == != <> > >= < <=`。数值或操作符非法时检测失败并记录诊断，不产生业务副作用。

## PK 点调整

```text
#ACT
CHANGEPKPOINT + 50
CHANGEPKPOINT - 20
CHANGEPKPOINT = 0
```

减少结果低于零时按零处理。负操作数、未知操作符或整数上溢会拒绝本次动作并保留旧值。

## 金币调整

```text
#ACT
GOLDCOUNT + 1000
GOLDCOUNT - 500
GOLDCOUNT = 2000
```

余额范围为 0 至 2,100,000,000。非法参数、下溢或上溢会拒绝本次动作，余额保持不变；成功增减继续使用现有金币同步消息。需要先判定余额时可使用既有 `CHECKGOLD 操作符 数量`。

真实语料高频使用的 `GIVEGOLD` 和 `TAKEGOLD` 继续可用：前者达到项目金币存储上限时只发放可容纳差额，后者请求超过余额时扣除现有全部余额。需要严格拒绝上下溢的迁移脚本应使用 `GOLDCOUNT`。

## 已知差异与排错

- 这些命令使用结构化失败并保持旧值，不静默接受非法数值。
- HP/MP 的双边界都比较当前值，不读取最大 HP/MP。
- 命令执行仍位于服务端主线程；脚本热重载只替换后续调用使用的快照。
- 参数错误日志以 `[TxtScripts]` 开头并包含当前页码。

## 物品检测、给予与回收

物品扩展语法只在 `TxtScriptsCompatibilityVersion` 以 `LFM2-` 开头时启用，避免改变既有 Crystal `CHECKITEM` 的第三参数含义。

```text
#IF
CHECKITEM 麻痹 1 1 0
#ACT
GIVE 回城卷 2
TAKE 麻痹 1 0 1 1 -1
```

`CHECKITEM` 的第三参数 `1` 表示部分名称匹配，第四参数必须为 `0`。`TAKE` 依次传入物品名、数量、改名检测、部分匹配、排除自定义 OK 框和持久模式；当前适配要求改名检测为 `0`、排除自定义 OK 框为 `1`。持久模式 `0` 不过滤、`-1` 只取满持久、`-2` 只取未满持久。回收前先核对总数，数量不足不会删除一部分物品。

基础 `GIVE 物品名 数量` 映射到现有 `GIVEITEM`。翎风扩展 `GIVE` 的极品位置、刺术、箭术、武力和特殊持久参数没有稳定的 LyoCrystal 等价字段，候选快照会失败关闭；迁移时改为基础 `GIVEITEM`，或由类型化 C# 物品 API 明确构造项目实际支持的属性。

既有 `GIVEITEM 物品名 数量` 与 `TAKEITEM 物品名 数量 [最低持久]` 保持 Crystal 行为，用于不需要部分名称、改名装备或自定义 OK 框的脚本。
