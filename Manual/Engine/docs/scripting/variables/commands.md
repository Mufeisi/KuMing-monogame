# 操作与显示命令

- 功能状态：实验性（当前支持 P/D/M/N/S/I/Call）
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
