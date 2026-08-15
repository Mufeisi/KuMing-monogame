# 操作与显示命令

- 功能状态：实验性（当前支持 P/D/M/N/S/I/Call/U/T/G/A/J/Z/HUMAN/GUILD/GLOBAL）
- 首次支持版本：开发版 2026-08-15

整数和小数共用以下命令。变量模块根据声明类型选择运算规则。

| 命令 | 用途 | 主要失败 |
|---|---|---|
| `MOV` | 赋值 | 类型不匹配、范围溢出 |
| `INC` | 增加 | 非数值、范围溢出 |
| `DEC` | 减少 | 非数值、范围溢出 |
| `MUL` | 乘法 | 非数值、范围溢出 |
| `DIV` | 除法 | 除零、结果不可表示 |
| `CHECK` | 比较 | 类型不兼容、引用无效 |
| `INITVAR` | 不存在时初始化 | 声明缺失、默认值非法 |
| `$STR` | 默认格式显示 | 引用无效 |
| `$FORMAT` | 固定小数位显示 | 位数越界、非数值 |
| `ROUND` | 四舍五入生成整数 | 溢出 |
| `FLOOR` | 向下取整 | 溢出 |
| `CEIL` | 向上取整 | 溢出 |
| `TRUNC` | 截去小数部分 | 溢出 |
| `PARSEDECIMAL` | 显式解析字符串 | 格式非法、小数位超限 |

当前已实现 `MOV/INC/DEC/MUL/DIV/CHECK`、`$STR/$FORMAT` 和 `ROUND/FLOOR/CEIL/TRUNC`。`INITVAR` 与 `PARSEDECIMAL` 随持久及字符串作用域阶段交付，当前不要使用。

## TXT NPC 写法

```text
MOV P0 5
DIV P0 2
MOV P.DropRate 12.5
INC P.DropRate 0.25
CHECK P.DropRate >= 12.75
```

显示写在 NPC 文本中：

```text
当前整数：<$STR(P0)>\
当前几率：<$FORMAT(P.DropRate,2)>%\
```

显式取整写入整数目标：

```text
MOV P0 FLOOR P.DropRate
```

VAR-02 的运行时前缀使用完全相同的命令：

```text
MOV D0 1
INC M0 2
MOV N$Score 3
MOV S$Label 在线
MOV I0 10
CHECK N$Score >= 3
<$STR(S$Label)>
```

私人持久变量使用同一套命令：

```text
MOV U0 10
INC U0 1
MOV T0 永久称号
MOV U.DropRate 1.25
INC U.DropRate 0.5
CHECK U.DropRate >= 1.75
<$FORMAT(U.DropRate,2)>%
<$STR(T0)>
```

每次成功修改 U/T 都会请求现有账户自动保存。脚本不需要也不应直接操作数据库。

全局持久变量也使用相同命令：

```text
MOV G0 10
INC G0 1
MOV G.EventRate 1.25
MUL G.EventRate 2
MOV A0 全服维护公告
MOV A.Notice 双倍经验活动已开启
<$FORMAT(G.EventRate,2)>
<$STR(A0)>
```

G/A 修改会请求服务器变量保存。原始 NPC `A*` 临时变量已移除，`MOV A0 ...` 不再写入人物的 `NPCVar`，而是直接写入全服持久字符串。

每日和自定义持久作用域仍使用相同命令：

```text
DEC J0 1
MOV Z0 已领取
INC HUMAN.LifetimeScore 0.5
INC GUILD.WarScore 10
MOV GLOBAL.SeasonRate 1.25
```

HUMAN/GUILD/GLOBAL 只能使用声明过的名称；写成 `HUMAN0` 会返回 `UnknownReference`。GUILD 缺少有效行会时返回 `ContextUnavailable`。

右操作数既可以是文化无关的字面量，也可以是同一上下文中的变量引用。固定编号 `P0-P999` 始终是 `Int64`；`MOV P0 1.5` 会返回类型错误，不会动态变成小数。

## 整数除法与小数除法

```text
MOV P0 1
DIV P0 4
```

结果是整数 `0`，保持兼容语义。

```text
VAR Decimal P Rate
MOV P.Rate 1
DIV P.Rate 4
```

结果是小数 `0.25`。

## 错误原子性

任何运算失败都不修改旧值。脚本可以根据稳定错误代码记录和处理失败，禁止出现“写入一半”或失败后变成零。
