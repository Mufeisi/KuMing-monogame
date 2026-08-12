# DB-03 SQLite 在线备份与状态监控

## 运行语义

服务器使用 Microsoft.Data.Sqlite 的在线 Backup API 从运行中的 WAL 数据库生成一致性副本，不复制正在变化的 `server.db`、`-wal` 与 `-shm` 文件。每次备份先写入同目录 `.partial` 临时文件，在该副本上执行 `PRAGMA integrity_check`，仅当结果为 `ok` 时原子改名为正式备份。异地副本同样先复制为 `.partial`、重新执行完整性检查，再原子发布。

服务启动后立即后台生成首份备份，此后按配置间隔自动执行。同一进程同时只允许一个备份；管理端重复触发返回冲突状态。关服或重启会停止定时器并等待正在进行的备份完成，避免新旧服务同时清理或写入同一目录。

正式备份文件统一命名为 `lyocrystal-sqlite-*.db`。保留策略只删除该命名空间下超出数量的旧文件，不碰其他文件；本地与异地目录分别执行相同保留数量。配置拒绝文件系统根目录、被现有文件占用的目录、本地/异地目录相同或互相嵌套，降低误删范围。服务构造时会实际创建、刷新并删除探针文件；本地状态目录或异地目录不可写时，正式宿主在进入 Ready 前失败关闭。

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
- 正式服使用 SQLite 时不允许关闭自动备份，且必须配置异地目录；开发/测试环境可以只生成本地副本。
- 正式门禁只接受 UNC 网络路径或与本地备份目录处于不同卷的路径，并在服务启动时验证目录真实可写。两个同卷兄弟目录不再被视为异地副本。UNC 后端及不同卷的物理隔离、容量和访问控制仍由部署人员在上线检查中确认。

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

状态包含 `State`、触发来源、最近尝试/成功 UTC 时间、耗时、本地/异地路径、完整性结果和失败摘要，并原子写入本地备份目录的 `backup-status.json`。若进程在状态为 `Running` 时退出，下次启动会把该次任务标记为 `Failed`，不会永久显示运行中；状态文件损坏或不可读同样恢复为明确的 `Failed`，不会静默重置为 `Idle`。失败同时写入服务日志。本地副本一经完整性检查和原子发布便先更新状态，再执行保留清理，因此后续清理失败也不会隐藏本次已生成的可用副本。

## 验证与阶段边界

T-08 的 DB-03 自动化部分在 WAL 源库存在未提交事务时执行在线备份，验证副本只包含最后已提交状态，并分别打开本地与异地副本读取数据、执行完整性检查；同时覆盖损坏副本拒绝、自动首备、保留清理及清理失败、状态跨实例保留、状态损坏、运行中断恢复、启动目录不可写或不可删的失败关闭以及真实管理端点角色边界。当前 Windows 验证环境从 `C:` 临时源目录向 `D:` 临时异地目录执行真实跨卷复制；无第二卷的测试环境仍验证 UNC 正式配置门禁。

DB-03 证明备份副本一致、完整且可由 SQLite 打开读取，但不把副本覆盖到空部署环境，也不模拟进程强制停止后的人工恢复步骤。空环境恢复、强停恢复、RPO/RTO 计时与每版本真实演练属于 DB-04；`SaveDelay` 生产强校验属于 DB-05。
