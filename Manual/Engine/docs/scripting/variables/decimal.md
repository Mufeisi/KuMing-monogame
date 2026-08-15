# 命名小数变量

- 功能状态：规划中
- 首次支持版本：尚未确定
- 数值实现：十进制 `Decimal`，不是二进制浮点

## 最小用法

```text
VAR Decimal U DropRate DEFAULT 1.0

MOV U.DropRate 12.5
INC U.DropRate 0.25
MUL U.DropRate 1.2
DIV U.DropRate 3
```

小数变量只在声明时比整数多一个 `Decimal` 类型，后续仍使用 `MOV/INC/DEC/MUL/DIV/CHECK`。

## 显示

```text
<$STR(U.DropRate)>
<$FORMAT(U.DropRate, 2)>
```

假设变量是 `12.5`：

- `$STR` 显示 `12.5`；
- `$FORMAT(..., 2)` 显示 `12.50`。

## 整数混合运算

整数与小数混合计算时结果提升为小数。写回整数必须显式取整：

```text
MOV U.Result ROUND U.DropRate
MOV U.Result FLOOR U.DropRate
MOV U.Result CEIL U.DropRate
MOV U.Result TRUNC U.DropRate
```

禁止静默截断，避免 `1.9` 在脚本作者不知情时变成 `1`。

## 几率单位

推荐以百分数点保存：

```text
MOV U.DropRate 12.5
你的掉落几率：<$STR(U.DropRate)>%
```

这里 `12.5` 明确表示 `12.5%`。概率判定由引擎转换为整数阈值，不使用 `double` 直接比较。

!!! warning "单位必须统一"
    同一个项目不能有时用 `0.125` 表示 `12.5%`，有时又用 `12.5` 表示 `12.5%`。功能页面和配置表必须明确单位。

## 字符串转换

字符串不会自动参与小数计算：

```text
MOV P.TempRate PARSEDECIMAL T0
```

解析失败时保留旧值并返回错误，不能默认为零。
