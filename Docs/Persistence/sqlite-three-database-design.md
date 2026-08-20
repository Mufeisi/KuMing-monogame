# SQLite 三库设计与迁移

- 状态：已实施
- 最后复核：2026-08-20
- 事实源：`Server/Persistence`、`Tools/DbMigrator`

## 运行接口

`IGamePersistence` 提供启动加载、单 Authority checkpoint、Identity 命令和 Character 命令。模块状态固定为 `Created -> Loading -> Ready | Faulted`，网络监听器只能在 `Ready` 后启动。

Character 启动数据在一个一致性读事务中先构造成 detached snapshot；必需表、完成 epoch、跨库账号/物品模板引用或任一查询失败时不应用快照。Guild、NPC Goods 和 Conquest 在地图与运行对象创建后、网络监听前应用。

## SQLite 连接基线

每个连接使用 private cache，并设置：

```sql
PRAGMA foreign_keys=ON;
PRAGMA journal_mode=WAL;
PRAGMA synchronous=FULL;
PRAGMA busy_timeout=10000;
```

## 物品所有权

`item_locations.item_id` 是主键。位置覆盖人物容器、账号仓库、邮件、拍卖、镶嵌、Guild 仓库、NPC 私人回购、公共二手商品和隔离区。checkpoint 捕获到重复位置时整体失败。

人物、Guild、邮件、拍卖和 NPC 持有的是 `item_instances`；World 只持有 `item_infos` 模板。World 发布会拒绝删除仍被 Character Runtime 引用的物品、地图、刷新、任务、技能、NPC 或城战定义。

## 一次性 World-only 迁移

迁移必须同时提供源库 SHA-256 和 `--authorize-reset`。工具先做源库 `integrity_check` 和只读备份，在 staging generation 创建三库，复制并校验 World 表，最后原子写 activation manifest。`next_ids` 只复制 8 个 World 键，`server_meta` 只复制 World 完成 epoch；Identity 和 Character 初始化为空。

```powershell
dotnet run --project Tools/DbMigrator/DbMigrator.csproj -- sha256 <source.db>
dotnet run --project Tools/DbMigrator/DbMigrator.csproj -- world-only-reset-players <source.db> <target-dir> <migration-id> <sha256> --authorize-reset
```

当前样例门禁：`item_infos=3938`、`monster_infos=1237`、`map_infos=626`、`map_respawns=4144`。

## 失败与回滚

- 未完成 generation 不写 activation manifest，因此不会被服务器选中。
- Character checkpoint 在单事务内更新钱包、人物、物品、容器、邮件、拍卖、Guild、NPC Goods、Buff 和 Conquest；任一步失败整体回滚。
- 归档只有数据库提交成功后才移除内存角色。
- `BACKUPPLAYER` 使用受控、版本化 JSON 与 SHA-256，排除账号、邮件、Guild 归属、在线对象、当前 Buff 和 World 模板对象；恢复时按稳定 ID 重绑物品、技能、任务和智能生物模板。`LOADPLAYER` 仅允许离线、ID 匹配且无外部物品所有权冲突的角色，并保留钱包、邮件、拍卖、Guild 和当前关系化 Buff 运行态。
