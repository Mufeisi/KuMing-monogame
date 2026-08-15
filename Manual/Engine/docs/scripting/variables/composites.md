# 列表 L$ 与字典 D$

- 功能状态：实验性
- 首次支持版本：开发版 2026-08-15（VAR-06）
- 生命周期：角色本次在线期间有效，小退或断线清除，不写入数据库

L$ 和 D$ 使用自定义名称，但不需要声明。它们用于脚本中间数据，不属于 D0 对话整数变量。

## 列表 L$

```text
MOV L$奖励 [金币,经验,装备]
MOV L$奖励[0] 元宝
INC L$奖励 称号
DEC L$奖励 经验
SENDMSG 6 全部：<$STR(L$奖励)>
SENDMSG 6 最后一项：<$STR(L$奖励[-1])>
```

索引从 `0` 开始。负索引从末尾计算，`-1` 是最后一项。索引越界时命令失败，原列表不变。

### 列表操作命令

| 命令 | 格式 | 说明 |
|---|---|---|
| `AddToList` | `AddToList L$变量 值` | 在末尾追加，等同整体 `INC` |
| `InsertToList` | `InsertToList L$变量 值 索引` | 在索引前插入；`-1` 表示追加 |
| `ReplaceListByIndex` | `ReplaceListByIndex L$变量 值 索引` | 替换一项 |
| `RemoveListByIndex` | `RemoveListByIndex L$变量 索引` | 按正或负索引删除 |
| `RemoveListByContent` | `RemoveListByContent L$变量 值 [区分大小写]` | 删除所有匹配项；默认区分大小写 |
| `GetListVarIndex` | `GetListVarIndex L$变量 值 N$目标` | 返回从 0 开始的索引；不存在返回 `-1` |
| `GetListVarCount` | `GetListVarCount L$变量 N$目标` | 返回项数 |
| `CheckVarInList` | `CheckVarInList L$变量 值` | 检查是否包含 |
| `CheckListAllDigit` | `CheckListAllDigit L$变量` | 检查全部项能否解析为十进制数 |
| `GetListMaxVar` | `GetListMaxVar L$变量 N$目标` | 获取数字最大值 |
| `GetListMinVar` | `GetListMinVar L$变量 N$目标` | 获取数字最小值 |
| `ReverseList` | `ReverseList L$源 L$目标` | 翻转列表 |
| `SortList` | `SortList L$源 L$目标 降序 文本排序` | 参数为 `0/1`；默认数字升序 |
| `ExtractList` | `ExtractList L$源 L$目标 起点 终点 [步长]` | 两端均包含；支持负索引和反向切片 |

```text
MOV L$数字 [66,77,11,33,22]
SORTLIST L$数字 L$升序 0 0
EXTRACTLIST L$升序 L$间隔 0 -1 2
```

## 字典 D$

```text
MOV D$积分 {张三:100,李四:200}
MOV D$积分[王五] 300
INC D$积分 赵六:400
DEC D$积分 张三
SENDMSG 6 王五积分：<$STR(D$积分[王五])>
```

键和值都是字符串，键区分大小写并保持插入顺序。整体赋值不允许重复键。

| 命令 | 格式 | 说明 |
|---|---|---|
| `GetDictKeyCount` | `GetDictKeyCount D$变量 N$目标` | 获取键数量 |
| `GetDictItems` | `GetDictItems D$变量 类型 L$目标` | 类型 `0` 取键，`1` 取值 |
| `CheckInDict` | `CheckInDict D$变量 值 [类型]` | 类型 `0` 查键，`1` 查值 |
| `CheckDictAllDigit` | `CheckDictAllDigit D$变量` | 检查所有值是否为十进制数 |
| `GetDictMaxValue` | `GetDictMaxValue D$变量 S$键目标 N$值目标` | 返回最大数字值所在项 |
| `GetDictMinValue` | `GetDictMinValue D$变量 S$键目标 N$值目标` | 返回最小数字值所在项 |

## 容量与失败行为

- 单个列表或字典最多 256 项。
- 每个项、键或值最多 1024 个 UTF-8 字节。
- 单个在线角色的全部 L$/D$ 内容合计最多 256 KiB。
- 超限、索引越界、格式错误、数字排序混入文本时返回结构化错误。
- 写入是原子的：失败不会截断集合，也不会留下部分修改。

!!! note "与持久变量的区别"
    L$/D$ 适合本次在线脚本的奖励候选、排序结果和临时映射。需要跨登录保存的数据应使用 U/T、HUMAN、GUILD 或 GLOBAL。
