# 每日周期变量

- 功能状态：实验性（VAR-05）
- 首次支持版本：开发版 2026-08-15
- 适用作用域：`J`、`Z`

J/Z 是人物私人持久变量，但每个配置周期只保留当期值：

| 形式 | 类型 | 是否声明 | 编号范围 |
|---|---|---|---|
| `J0-J499` | `Int64` | 否 | 0-499 |
| `J.名称` | `Integer` 或 `Decimal` | 是 | 仅命名 |
| `Z0-Z499` | 字符串 | 否 | 0-499 |
| `Z.名称` | `String` | 是 | 仅命名 |

适合每日副本次数、每日积分、签到阶段和今日活动文本。需要永久累计的数据应使用 U/T 或 HUMAN。

## 重置时间

在 `Configs/Setup.ini` 配置服务器本地时间的重置小时：

```ini
[CSharpScripts]
ScriptVariableDailyResetHour=0
```

范围是 0-23，默认 0 表示本地午夜。非法值会阻止服务启动，不按机器环境猜测。修改配置后应在维护窗口重启；新的周期边界按修改后的小时计算。

引擎为每个角色保存周期号。服务端在线跨过边界时分批检查全部人物和英雄；停服跨过一天或多天时，首次读取或修改前也会延迟清除。时钟向过去回拨不会恢复旧周期的数据，也不会再次发放已经清除的值。

## 使用方法

固定编号无需声明：

```text
MOV J0 3
DEC J0 1
MOV Z0 已领取
CHECK J0 > 0
剩余次数：<$STR(J0)>，状态：<$STR(Z0)>
```

每日小数先在启动 C# 脚本声明：

```csharp
registry.RegisterVariable(
    ScriptVariableScope.J,
    "DailyRate",
    ScriptVariableKind.Decimal,
    "1.0");
```

TXT NPC 仍使用相同命令：

```text
INC J.DailyRate 0.25
今日倍率：<$FORMAT(J.DailyRate,2)>
```

## 保存位置与边界

- Legacy：随角色二进制档案保存，CustomVersion 为 2；旧 CustomVersion 0/1 档案仍可读取。
- SQLite/MySQL：`character_script_variables`，`reset_policy=Daily`，周期写入 `reset_period_id`。
- 角色归档/恢复携带 J/Z 和周期号；若恢复时已进入新周期，首次访问立即清除旧值。
- 清除或写入后请求现有账户自动保存；异常断电窗口仍由账户自动保存周期决定。

!!! warning "不要用登录脚本重复 MOV 初始化"
    `MOV J0 3` 放在登录 QM 会在每次登录时重置次数。固定编号默认值为 0；命名变量请用声明默认值，需要首次写入存储时使用 `INITVAR J.名称`，它不会覆盖已有值。
