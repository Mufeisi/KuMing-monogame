# SQLite 三库备份、恢复与回滚 Runbook

- 状态：已实施
- 最后复核：2026-08-20
- 事实源：`SqliteDatabaseLayout`、`SqliteLayoutMigrator`

## 备份

1. 阻止新登录并执行成功的 Character shutdown checkpoint；失败时不得继续停服清理。
2. 停止服务器，确认没有进程持有数据库写连接。
3. 读取 `<SqliteDirectory>/database-layout.json`，解析其 `generationDirectory`；不得自行选择某个历史目录。
4. 将 activation manifest 和该 generation 下的 `identity.db`、`characters.db`、`world.db` 复制到同一只读备份目录。
5. 对三个副本分别执行 `PRAGMA integrity_check` 和 `PRAGMA foreign_key_check`，记录文件 SHA-256。

## 恢复

1. 保持服务器停止，先备份当前 activation manifest 和当前 generation。
2. 将待恢复的完整三库 generation 放入 `<SqliteDirectory>/layouts/<restore-id>`；禁止混用不同备份的单库文件。
3. 对三库执行 `integrity_check=ok`、空 `foreign_key_check`，并检查各自 `database_manifest.completed=1`。
4. 在同一文件系统中写临时 activation manifest，完整刷新后原子替换 `database-layout.json`。
5. 启动服务器；若 manifest 不完整、路径越界、文件缺失或持久化未进入 `Ready`，服务器必须拒绝开放网络。

## 迁移失败回滚

迁移失败时不要修改现有 activation manifest。删除 staging 不是恢复前提；保留它用于诊断或按相同 migration ID 续跑。源库和迁移前只读备份是恢复事实源。

## 运行故障诊断

- `startup_load_failed`：检查三个 manifest、Schema version、Character 完成 epoch 和必需表。
- `character_commit_failed`：保留 dirty 运行态，检查 SQLite busy/磁盘空间/外键错误；关服流程应继续阻断。
- `world_commit_failed`：检查错误中的稳定 ID，修复 Character 引用或恢复对应 World 定义后再发布。
- `backup_item_ownership_conflict`：物品已被仓库、邮件、拍卖、Guild 或 NPC 等外部聚合持有，不得用人物备份覆盖。
- `backup_world_definition_missing`：备份引用的物品模板已从 World 删除或缺失；先恢复对应 World generation，不得把无模板物品写回 Character。
