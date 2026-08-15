# 自定义持久作用域

- 功能状态：实验性（VAR-05）
- 首次支持版本：开发版 2026-08-15
- 适用作用域：`HUMAN`、`GUILD`、`GLOBAL`

三种作用域都只支持显式命名变量，不存在 `HUMAN0`、`GUILD0` 或 `GLOBAL0`。当前允许 `Integer` 与最大 8 位小数的 `Decimal`；文本请分别使用 T、A 或其他明确字符串作用域。

| 作用域 | 所有者 | 共享范围 | 持久位置 |
|---|---|---|---|
| `HUMAN.名称` | 单个角色 | 仅该角色 | 角色档案 / `character_script_variables` |
| `GUILD.名称` | 行会 | 同一行会成员 | 行会档案 / `guild_script_variables` |
| `GLOBAL.名称` | 当前服务器 | 全服 | `Server.Variables.json` / `server_script_variables` |

HUMAN 与 U 都是角色永久数值；HUMAN 适合可读的业务名称，U 同时提供翎风编号槽位。GLOBAL 与 G 的生命周期相同；GLOBAL 强制命名，适合新功能，G 同时兼容固定编号。不要建立意义相同的两份变量。

## 声明

在服务器启动 C# 脚本中统一注册，不需要人物登录或行会在线：

```csharp
registry.RegisterVariable(
    ScriptVariableScope.Human,
    "LifetimeScore",
    ScriptVariableKind.Decimal,
    "0");

registry.RegisterVariable(
    ScriptVariableScope.Guild,
    "WarScore",
    ScriptVariableKind.Integer,
    "0");

registry.RegisterVariable(
    ScriptVariableScope.Global,
    "SeasonRate",
    ScriptVariableKind.Decimal,
    "1.0");
```

新增声明支持现有 C# 脚本原子热重载。修改类型会被拒绝；修改默认值只影响尚未写入的所有者。

## TXT NPC 用法

```text
INC HUMAN.LifetimeScore 0.5
INC GUILD.WarScore 10
MOV GLOBAL.SeasonRate 1.25

个人总分：<$FORMAT(HUMAN.LifetimeScore,2)>
行会战分：<$STR(GUILD.WarScore)>
赛季倍率：<$FORMAT(GLOBAL.SeasonRate,2)>
```

访问 GUILD 时人物必须属于有效行会，否则返回 `ContextUnavailable` 且不写值。HUMAN 需要角色上下文；GLOBAL 可由服务器启动脚本或任意 NPC 访问。

## 行会生命周期与排行索引

GUILD 值跟随 `GuildInfo` 保存，改名不会改变所有权；成员加入或退出只改变访问资格，不复制数据。行会解散时随行会档案和 SQL 关系数据一起失去业务所有者，备份恢复时随行会恢复。

第 20 号 SchemaMigration 为 HUMAN/GUILD/GLOBAL 数值建立 `作用域 + 变量名 + 整数值` 排行索引。当前脚本层未开放通用排行命令；运营查询或未来排行榜 API 只能对已声明的 Integer 使用该索引。Decimal 排行未开放，禁止按十进制文本做字符串排序。

## 保存与备份

- HUMAN 修改请求账户自动保存。
- GUILD 修改将行会标记为待保存，进入现有行会保存流程。
- GLOBAL 修改请求服务器变量保存。
- Legacy 行会和角色档案的 CustomVersion 已提升到 2；升级前必须备份。
- SQLite/MySQL 第 20 号迁移新增 `guild_script_variables` 和三个排行索引。
- `Server.Variables.json`、角色/行会档案或 SQL 主库都属于运行数据，发布更新不得覆盖。
