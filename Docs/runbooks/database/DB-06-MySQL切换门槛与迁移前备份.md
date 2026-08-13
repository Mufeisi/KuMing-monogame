# DB-06 MySQL 切换门槛与迁移前备份

## 结论

SQLite 仍是当前默认数据库。只有下列任一指标同时达到数值门槛和持续窗口，才进入 MySQL 迁移规划；未达到或只有单次尖峰时必须维持 SQLite：

| 维度 | 数值门槛 | 持续窗口 | 数据来源 |
|---|---:|---:|---|
| 峰值在线玩家 | ≥ 500 | 连续 7 个自然日 | `ActiveConnections` 日度峰值 |
| SQLite 主库大小 | ≥ 10 GiB | 连续 3 个自然日 | 每日同一时点的主库文件大小 |
| 保存事务延迟 | `SaveTransactionCommit` P95 ≥ 750ms | 连续 3 个自然日 | PERF-00 性能快照 |
| 保存失败 | ≥ 3 次/小时 | 连续 3 个小时 | `SaveFailure` 指标与保存失败日志 |

`MySqlSwitchPolicy.Assess` 固化上述边界并返回 `MaintainSqlite` 或 `PlanMySqlMigration`。备份入口不接受外部判定结果，而是直接接收原始指标并重新判定。判定只允许使用完整窗口数据；重启、缺测或口径变化会中断连续窗口，不能用估算值补齐。

## 迁移前强制备份

DB-06 不新增 MySQL 数据迁移命令。后续迁移实现必须先调用 `MySqlSwitchPolicy.CreateRequiredPreMigrationBackup`，且只能在门槛已经触发后调用。该门禁不会复用历史成功状态，而是通过 DB-03 的 `SqliteBackupService.RunNow` 以唯一触发标识现场生成一组新副本，并同时要求：

1. 触发来源以 `mysql-migration-preflight:` 开头并包含本次唯一标识，执行状态为成功；
2. 本地副本和异地副本均已发布且文件存在；
3. 异地副本必须是 UNC 或与本地副本位于不同存储卷；
4. DB-03 已报告 `integrity_check=ok`，门禁随后再次对两份文件执行完整 `PRAGMA integrity_check`。

创建入口还会要求备份服务的规范化 `SourcePath` 与当前待切换的 `Settings.SqlitePath` 完全一致，避免用其他小型数据库生成授权。成功后会在原 SQLite 主库路径旁原子写入 `server.db.mysql-switch-authorization.dpapi`，其中包含格式版本、规范化源路径、原始指标、唯一备份标识、两份副本的绝对路径和 SHA-256；整个载荷使用 Windows DPAPI `CurrentUser` 保护，普通配置编辑无法手写 JSON 冒充。该运行时记录由既有 `*.dpapi` 规则排除在 Git 外。正式 `ServerPersistenceFactory.CreateFromSettings()` 选择 MySQL 前会解密记录、核对格式版本和当前源路径、重新计算门槛，并复验路径隔离、两份数据库完整性及文件摘要；记录缺失、损坏、来自其他源库或副本被改动时一律拒绝 MySQL。因此仅修改 `Setup.ini` 的 provider 不能绕过门禁。任一条件失败时后续迁移不得开始。

## 运维执行

1. 按固定口径保存每日/每小时指标，不因短期活动峰值切库。
2. 使用同一组窗口数据调用判定策略；结果为 `MaintainSqlite` 时结束评估并继续 SQLite。
3. 结果为 `PlanMySqlMigration` 时先评审容量、成本、回滚与停机窗口，再编写独立迁移方案。
4. 迁移程序接入强制备份门禁；保留生成的授权记录及其本地、异地路径，正式 provider 首次和后续启动都会复验。
5. 迁移验证、双写、回切与 Schema 映射不属于 DB-06，必须作为独立后续任务验收。

## 边界

本任务只定义并执行“是否进入迁移规划”“迁移开始前必须新建可验证备份”和“正式 MySQL provider 必须持有可复验授权”。它不自动切换 `DatabaseProvider`，不复制业务数据到 MySQL，不新增表或迁移工具，也不把“达到门槛”等同于“必须立即切库”。升级后已有 MySQL 配置若没有该授权记录也会失败关闭，运维必须先保留现状并按独立迁移方案生成授权，不允许用手工改 provider 绕过。DB-04、DB-05 尚未完成时，GATE-P3 仍保持开启状态。
