# 全局持久变量与数据库

- 功能状态：实验性（VAR-04）
- 首次支持版本：开发版 2026-08-15
- 适用作用域：`G`、`A`

## 用途与类型

G/A 是当前服务器内所有人物、NPC 和 C# 脚本共享的持久状态：

| 形式 | 类型 | 是否声明 | 示例 |
|---|---|---|---|
| `G0-G999` | `Int64` | 否 | 全服计数、活动开关 |
| `G.名称` | `Integer` 或 `Decimal` | 是 | `G.EventRate` |
| `A0-A999` | 字符串 | 否 | 公告文本 |
| `A.名称` | `String` | 是 | `A.Notice` |

固定编号和命名变量使用同一套 `MOV/INC/DEC/MUL/DIV/CHECK/$STR/$FORMAT` 命令。字符串 A 只允许赋值、比较和显示，不参与数值运算。

!!! warning "A* 语义已经统一"
    原始代码中人物 NPC 对话私有的 `A*` 临时槽位已经取消。从 VAR-04 起，`MOV A0 文本` 与 `MOV A.Notice 文本` 都写入全服持久字符串；升级前若把 A0 当临时变量使用，必须检查其共享和跨重启影响。

## 数据保存在哪里

G/A 跟随服务端当前数据库 Provider：

| Provider | 实际存储 | 恢复方式 |
|---|---|---|
| `Legacy` | 服务端目录 `Server.Variables.json` | 主文件损坏时自动尝试 `.bak` |
| `SQLite` | `server_script_variables` 表 | 随 SQLite 数据库备份恢复 |
| `MySQL` | `server_script_variables` 表 | 随 MySQL 一致性备份恢复 |

Legacy 保存先写入 `.tmp` 并刷新磁盘，再保留上一版为 `.bak`，最后原子替换主文件。JSON 含版本号，并在采用前完整检查作用域、键、类型和值；主文件无效时才读取备份。SQLite/MySQL 由第 19 号 SchemaMigration 创建关系表，以 `variable_namespace + variable_key` 为主键。

Decimal 不存数据库浮点数，而以文化无关十进制文本保存，重启前后的几率值不会产生二进制浮点尾差。

## 声明与热重载

固定编号无需声明。命名变量在 C# 脚本模块注册：

```csharp
registry.RegisterVariable(
    ScriptVariableScope.G,
    "EventRate",
    ScriptVariableKind.Decimal,
    "1.0");

registry.RegisterVariable(
    ScriptVariableScope.A,
    "Notice",
    ScriptVariableKind.String,
    "暂无活动");
```

声明属于服务器配置，不依赖在线人物，因此在服务器启动脚本中统一注册。新增声明随现有 C# 脚本热重载原子发布，不要求重启；失败时保留旧声明。修改默认值不会覆盖已经保存的实际值，修改类型或作用域会被拒绝。

## TXT 使用示例

```text
MOV G0 100
INC G0 1
MOV G.EventRate 1.25
MUL G.EventRate 2
CHECK G.EventRate >= 2.5

MOV A0 全服维护公告
MOV A.Notice 双倍经验活动已开启

当前人数累计：<$STR(G0)>
活动倍率：<$FORMAT(G.EventRate,2)>
公告：<$STR(A.Notice)>
```

每次成功修改或清空 G/A 后，引擎请求服务器变量自动保存。该请求由当前 Provider 执行；异常断电前尚未刷盘的窗口仍取决于现有自动保存周期。正常关服会执行最终保存。

## 限额与异常处理

- G/A 合计最多保存 8192 项实际值。
- 键名忽略大小写，保存时规范为大写；编号范围是 `0-999`。
- G 只接受整数或 Decimal，A 只接受字符串。
- 单次操作失败不会改变旧值。
- SQL 加载遇到单条损坏记录时跳过该行；Legacy 主文件和备份都无效时拒绝用默认值覆盖已有事实，并记录加载失败。

运维人员不应直接编辑数据库变量表或 JSON。需要改变变量类型时，应建立显式数据迁移并在升级前备份。
