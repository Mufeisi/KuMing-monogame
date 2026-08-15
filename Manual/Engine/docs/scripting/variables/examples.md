# 完整使用示例

- 功能状态：混合；VAR-01～05 的运行时和持久作用域示例可用
- 首次支持版本：开发版 2026-08-15

## 在线与地图临时变量

```text
MOV N$击杀计数 10
INC N$击杀计数 1
MOV S$任务阶段 进行中
MOV M0 100
当前在线击杀：<$STR(N$击杀计数)>，阶段：<$STR(S$任务阶段)>
```

切换 NPC 时这些值保留；换地图只清 `M0`，小退或掉线清 `N$击杀计数` 与 `S$任务阶段`。

## 私人持久掉落几率

服务器启动时由 C# 脚本模块注册一次：

```csharp
registry.RegisterVariable(
    ScriptVariableScope.U,
    "DropRate",
    ScriptVariableKind.Decimal,
    "1.0");
```

登录 QM 只读取默认值，不必为每个人重复声明：

```text
[@Login]
SENDMSG 6 你的基础掉落几率为：<$FORMAT(U.DropRate, 2)>%
```

活动奖励：

```text
INC U.DropRate 0.5
SENDMSG 6 掉落几率提升后：<$FORMAT(U.DropRate, 2)>%
```

`U` 是人物私人持久作用域，不同人物拥有不同的 `DropRate`。首次成功修改后进入现有自动保存流程，掉线、重启和归档恢复后仍保留。

## 全服活动倍率热更新

首次声明：

```text
VAR Decimal G EventRate DEFAULT 1.0
```

运行中修改声明文件并保存后，引擎可以热重载新增变量，不需要重启。修改默认值不会覆盖已经保存的 `G.EventRate`。

使用：

```text
MOV G.EventRate 2.5
CHECK G.EventRate > 1.0
SENDMSG 0 当前全服活动倍率：<$FORMAT(G.EventRate, 2)>
MOV A.Notice 双倍经验活动已开启
SENDMSG 0 <$STR(A.Notice)>
```

G/A 是服务器级共享状态，任意人物触发修改后，其他人物立即可见；正常重启后从当前 Provider 恢复。固定 `A0-A999` 与命名 `A.名称` 都属于统一全局字符串存储，不再存在原始 NPC 私有 A 槽位。

## NPC 对话临时计算

```text
VAR Decimal P PreviewRate DEFAULT 0

MOV P.PreviewRate U.DropRate
MUL P.PreviewRate G.EventRate
SENDMSG 6 本次预览几率：<$FORMAT(P.PreviewRate, 2)>%
```

关闭或切换 NPC 后，`P.PreviewRate` 自动清除；`U.DropRate` 和 `G.EventRate` 保留。

## 每日次数与永久累计

```text
CHECK J0 > 0
DEC J0 1
INC HUMAN.LifetimeRuns 1
SENDMSG 6 今日剩余：<$STR(J0)>，永久完成：<$STR(HUMAN.LifetimeRuns)>
```

J0 在配置的每日边界清除；HUMAN.LifetimeRuns 永久保留。不要在登录 QM 中用 `MOV J0` 重设次数，后续条件初始化能力开放前应由活动逻辑明确发放。

## 行会与赛季倍率

```text
INC GUILD.WarScore 10
MOV GLOBAL.SeasonRate 1.25
SENDMSG 6 行会积分：<$STR(GUILD.WarScore)>
SENDMSG 0 当前赛季倍率：<$FORMAT(GLOBAL.SeasonRate,2)>
```

同一行会成员立即看到相同 WarScore；无行会人物执行 GUILD 命令会失败且不改值。GLOBAL 是服务器范围共享状态，正常重启后保留。
