# DB-03 SQLite 在线备份与状态监控

## 运行语义

服务器使用 Microsoft.Data.Sqlite 的在线 Backup API 从运行中的 WAL 数据库生成一致性副本，不复制正在变化的 `server.db`、`-wal` 与 `-shm` 文件。每次备份先写入同目录 `.partial` 临时文件，在该副本上执行 `PRAGMA integrity_check`，仅当结果为 `ok` 时原子改名为正式备份。异地副本同样先复制为 `.partial`、重新执行完整性检查，再原子发布。

服务启动后立即后台生成首份备份，此后按配置间隔自动执行。同一进程同时只允许一个备份；管理端重复触发返回冲突状态。关服或重启会停止定时器并等待正在进行的备份完成，避免新旧服务同时清理或写入同一目录。

正式备份文件统一命名为 `lyocrystal-sqlite-*.db`。保留策略只删除该命名空间下超出数量的旧文件，不碰其他文件；本地与异地目录分别执行相同保留数量。配置拒绝文件系统根目录以及本地/异地目录相同或互相嵌套，降低误删范围。

## 配置

`Setup.ini` 的 `[Database]` 节支持：

```ini
SqliteBackupEnabled=True
SqliteBackupDirectory=.\Backups\SQLite
SqliteBackupOffsiteDirectory=\\backup-server\LyoCrystal\SQLite
SqliteBackupIntervalMinutes=60
SqliteBackupRetentionCount=48
```

- 间隔允许 1 分钟～7 天；保留数量允许 1～10000。
- 正式服使用 SQLite 时不允许关闭自动备份，且必须配置非空异地目录；开发/测试环境可以只生成本地副本。
- `SqliteBackupOffsiteDirectory` 应指向另一台主机、独立存储卷或由外部同步保护的挂载目录。程序只能验证目录不与本地备份目录重叠，无法仅凭路径证明物理异地属性，部署人员必须在上线检查中确认。

## 一键触发与状态

管理 HTTP 服务复用 SEC-04/05 的可信来源、Bearer 角色鉴权与可靠审计日志：

- `POST /backup/run`：仅 Administrator 可触发；成功排队返回 `202 Accepted`，已有任务时返回 `409 Conflict`。
- `GET /backup/status`：Administrator 与 Operator 可查询。

PowerShell 示例：

```powershell
$headers = @{ Authorization = "Bearer <Administrator令牌>" }
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:7777/backup/run" -Headers $headers
Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:7777/backup/status" -Headers $headers
```

状态包含 `State`、触发来源、最近尝试/成功 UTC 时间、耗时、本地/异地路径、完整性结果和失败摘要，并原子写入本地备份目录的 `backup-status.json`。若进程在状态为 `Running` 时退出，下次启动会把该次任务标记为 `Failed`，不会永久显示运行中。失败同时写入服务日志。

## 验证与阶段边界

T-08 的 DB-03 自动化部分在 WAL 源库存在未提交事务时执行在线备份，验证副本只包含最后已提交状态，并分别打开本地与异地副本读取数据、执行完整性检查；同时覆盖损坏副本拒绝、自动首备、保留清理、状态跨实例保留、运行中断恢复以及真实管理端点角色边界。

DB-03 证明备份副本一致、完整且可由 SQLite 打开读取，但不把副本覆盖到空部署环境，也不模拟进程强制停止后的人工恢复步骤。空环境恢复、强停恢复、RPO/RTO 计时与每版本真实演练属于 DB-04；`SaveDelay` 生产强校验属于 DB-05。
