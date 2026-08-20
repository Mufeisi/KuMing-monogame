# ADR-0001：SQLite 三权物理拆分

- 状态：已接受并实施
- 决策日期：2026-08-20
- 最后复核：2026-08-20
- 事实源：`Server/Persistence/Sql/AuthoritySchemaMigrator.cs`

## 上下文

旧实现把账号、人物运行态、世界定义和二进制兼容数据放在同一事实源中，保存周期、故障域和访问权限无法独立控制。Legacy 文件回退还会在 SQL 数据缺失或读取失败时掩盖错误。

## 决定

使用三个物理 Authority：

| 数据库 | 权属 |
|---|---|
| `identity.db` | 账号名、密码哈希和盐、资料、登录、封禁与权限 |
| `characters.db` | 钱包、角色、英雄、物品实例、公会、邮件、拍卖、NPC 商品、城战运行态、备份与归档 |
| `world.db` | 地图、模板、怪物、NPC、任务、技能、商城、攻城和刷新定义 |

每库独立维护 `schema_version` 与 `database_manifest`。跨库不承诺 ACID；接口不提供 `All` checkpoint。跨库引用使用稳定 ID，并在启动及 World 发布前执行应用级完整性检查。

SQLite 是当前发布门禁。MySQL 保留三连接字符串与同构 Adapter，但本次不迁移旧单库 MySQL。

## 替代方案

- 单库按表分区：部署简单，但不能隔离文件备份、写锁和权限边界。
- 每角色一个数据库：故障域过碎，公会、邮件、拍卖和物品唯一所有权需要昂贵的跨库协调。
- 保留 Legacy 回退：迁移容易，但会形成双事实源并隐藏不完整 SQL 数据。

## 后果

- Identity 安全变更即时提交，Character 使用统一周期 checkpoint，World 只显式保存。
- 数据库文件可独立备份和恢复，但恢复时必须保持 generation 一致并执行跨库检查。
- 删除角色改为生命周期转换，资产原地保留。
- 在显式 tombstone 和完整快照证明齐备前，运行态保存对人物、物品和位置采用保守 upsert，不因一次快照缺失自动物理删除资产。
- Guild、NPC Goods、Buff 与 Conquest 行记录 snapshot lineage；当前不把“某次快照未出现”等同于删除。淘汰必须由完整域完成标记或显式业务删除命令驱动。
