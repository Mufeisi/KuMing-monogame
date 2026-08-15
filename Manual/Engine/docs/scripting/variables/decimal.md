# 命名小数变量

- 功能状态：实验性（当前支持 P/D/M/N/I/U/G/J/HUMAN/GUILD/GLOBAL/Call）
- 首次支持版本：开发版 2026-08-15
- 数值实现：十进制 `Decimal`，不是二进制浮点

## 最小用法

先在启动或 QM 文本的加载期声明 `N.DropRate` 为 Decimal，随后 TXT 使用：

```text
VAR Decimal N DropRate DEFAULT 0
MOV N.DropRate 12.5
INC N.DropRate 0.25
MUL N.DropRate 1.2
DIV N.DropRate 3
```

小数变量只在声明时比整数多一个 `Decimal` 类型，后续仍使用 `MOV/INC/DEC/MUL/DIV/CHECK`。

## 显示

```text
<$STR(P.DropRate)>
<$FORMAT(P.DropRate,2)>
```

假设变量是 `12.5`：

- `$STR` 显示 `12.5`；
- `$FORMAT(..., 2)` 显示 `12.50`。

## 整数混合运算

整数与小数混合计算时结果提升为小数。写回整数必须显式取整：

```text
MOV P0 ROUND P.DropRate
MOV P0 FLOOR P.DropRate
MOV P0 CEIL P.DropRate
MOV P0 TRUNC P.DropRate
```

禁止静默截断，避免 `1.9` 在脚本作者不知情时变成 `1`。

## 几率单位

推荐以百分数点保存：

```text
MOV N.DropRate 12.5
你的掉落几率：<$STR(N.DropRate)>%
```

这里 `12.5` 明确表示 `12.5%`。概率判定由引擎转换为整数阈值，不使用 `double` 直接比较。具体命令和三种显式单位见[公式、概率与格式化](formula-probability.md)。

!!! warning "单位必须统一"
    同一个项目不能有时用 `0.125` 表示 `12.5%`，有时又用 `12.5` 表示 `12.5%`。功能页面和配置表必须明确单位。

## 字符串显式转换

字符串不会自动参与小数计算：

```text
MOV P.TempRate PARSEDECIMAL T0
```

`PARSEDECIMAL` 已实现，来源必须是 String 变量，目标必须是 Decimal 变量。它只接受文化无关格式，例如 `12.5`；解析失败、超过 8 位小数或目标类型错误时保留目标旧值，不会默认为零。
