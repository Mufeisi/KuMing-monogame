# 完整使用示例

- 功能状态：混合；运行时作用域和 U/T 私人持久示例可用，全局持久示例仍为规划中
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
CHECK G.EventRate > 1.0
SENDMSG 0 当前全服活动倍率：<$FORMAT(G.EventRate, 2)>
```

## NPC 对话临时计算

```text
VAR Decimal P PreviewRate DEFAULT 0

MOV P.PreviewRate U.DropRate
MUL P.PreviewRate G.EventRate
SENDMSG 6 本次预览几率：<$FORMAT(P.PreviewRate, 2)>%
```

关闭或切换 NPC 后，`P.PreviewRate` 自动清除；`U.DropRate` 和 `G.EventRate` 保留。
