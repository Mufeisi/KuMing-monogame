# 公式、概率与格式化

- 功能状态：实验性
- 首次支持版本：开发版 2026-08-15（VAR-06）
- 数值模型：十进制 `Decimal`，最多 8 位小数

## FORMULATION 公式

```text
FORMULATION (P.BaseRate + P.BonusRate) * 1.5 P.ResultRate
FORMULATION Random(1,20)^2 P.RandomResult
```

目标必须是已存在的整数或 Decimal 变量。公式支持四则运算、括号、一元正负号、0–28 整数幂、数值变量引用，以及包含两个端点的 `Random(min,max)`。

整数目标只接受没有小数部分且在 `Int64` 范围内的结果。`1/2` 写入整数目标会失败，不会静默变成 `0`；需要取整时先写入 Decimal，再使用 `ROUND/FLOOR/CEIL/TRUNC`。

### 安全边界

- 公式最长 1024 个字符、最多 256 个词元、括号最多嵌套 32 层。
- `Random` 只接受整数上下界，最小值不能大于最大值。
- 除零、溢出、未知变量、字符串或复合变量参与计算都会失败。
- 解析器不能调用 .NET 方法、反射、文件、网络或进程。
- 中间结果按 8 位小数进行十进制舍入；失败时目标旧值保持不变。

## CHANCE 概率

```text
#IF
CHANCE P.DropRate PERCENT
#ACT
SENDMSG 6 本次判定命中
```

| 单位 | 合法范围 | 示例含义 |
|---|---:|---|
| `PERCENT` | 0–100 | `12.5` 表示 12.5% |
| `BASISPOINTS` | 0–10000 | `1250` 表示 12.5% |
| `FRACTION` | 0–1 | `0.125` 表示 12.5% |

省略单位时默认为 `PERCENT`。引擎把数值转换为百万分辨率的整数阈值，再使用服务端随机源判定，不用 `double` 比较。`0` 永不命中，最大值始终命中，越界数值直接报错。

!!! warning "不要混用单位"
    `CHANCE P.Rate PERCENT` 中的 `0.5` 是 0.5%；只有 `FRACTION` 中的 `0.5` 才是 50%。建议项目统一使用 `PERCENT`。

## 显示

```text
当前几率：<$STR(P.DropRate)>%
当前几率：<$FORMAT(P.DropRate,2)>%
```

`$STR` 去掉无意义尾零；`$FORMAT` 固定显示 0–8 位小数。显示格式不会改变变量保存值。
