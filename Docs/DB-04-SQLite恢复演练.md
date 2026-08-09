# DB-04 SQLite 恢复演练

## 恢复入口

恢复复用正式 `Server.MirForms` 宿主，不新增独立产品工具。服务器必须先停止，再执行：

```powershell
dotnet Server.dll --restore-sqlite "<DB-03 已验证备份.db>" --target "<Data\server.db>"
```

省略 `--target` 时，宿主读取 `Setup.ini` 的 `[Database] SqlitePath`。未知参数返回退出码 `2`；恢复校验或文件操作失败返回 `1`；成功返回 `0`。

## 恢复与回滚边界

1. 来源必须是 DB-03 生成的独立主库文件；若同名 `-wal` 或 `-shm` 存在则失败关闭。
2. 来源先执行完整 `PRAGMA integrity_check`，再复制到目标目录唯一 `.partial`，执行 WriteThrough、`Flush(true)` 和第二次完整性检查。
3. 强停目标若带 WAL，先在离线独占条件下执行 `wal_checkpoint(TRUNCATE)`，再切换 `journal_mode=DELETE`。这会把已提交 WAL 状态收敛进主库；无法排空则中止恢复。
4. 收敛后的旧主库复制、刷新并校验为 `.pre-restore-<代次>` 单文件回滚工件；目标残余 WAL/SHM 必须严格删除。
5. 已有目标用同目录 `File.Replace` 原子发布，空环境用同目录 `File.Move` 原子发布。任一进程中断点上，正式目标都是可独立打开的旧库或新库，不存在必须靠进程内 `catch` 拼回三文件组的窗口。
6. 发布后最终校验失败时，从已验证回滚副本再次原子替换；任何回滚失败都会汇总报告“回滚不完整”，不会伪报成功。

正在使用的目标库会在任何改动前失败关闭。回退时只需把对应的单文件 `.pre-restore-<代次>` 作为恢复来源再次执行同一正式命令。

## 每版本演练步骤

1. 从 `/backup/status` 记录最近一次 `Succeeded` 的 `LastSuccessUtc`、本地和异地路径，确认本地/异地副本完整性与哈希。
2. 制造备份后的已提交业务变化，在 WAL/SHM 存在时记录进程 PID 并 `Stop-Process -Force`。
3. 记录故障时刻、主库/WAL/SHM 清单与哈希；副本成功时刻到故障时刻的差值为本次可复算 RPO。
4. 执行正式恢复命令，捕获 stdout、stderr 和退出码。
5. 用正式持久化加载器核对账号、角色、行会、商品、攻城数据，确认 `integrity_check=ok`，并让隔离网络/脚本/资源的现有宿主生命周期进入 `Ready`。恢复命令开始到 `Ready` 的差值为 RTO；必须不超过 30 分钟。
6. 归档原始 PowerShell transcript、命令、UTC 时间、PID、文件清单、哈希、读取值、退出码、提交版本和回滚工件位置。

每个拟发布版本至少归档一次真实演练记录；自动化单测不能替代后续版本的真实演练。

## 当前版本结果与阶段边界

2026-08-10 Windows Release 演练使用正式 DB-03 `SqliteBackupService` 从实际五域 SQLite 库生成 C: 本地副本和 D: 异卷副本，两份 SHA-256 一致且 `integrity_check=ok`。备份后把账号金币从 100 提交为 777，并在 WAL/SHM 存在时强制终止持有进程；正式 `Server.dll --restore-sqlite` 返回 0。恢复后账号金币回到备份值 100，账号、角色、行会、商品、攻城五域均由现有加载接缝复读，宿主进入 `Ready`。

- RPO：303ms（最后成功备份到强停故障）。
- RTO：600ms（恢复命令开始到业务域复读及 `Ready`）；故障到 `Ready` 为 625ms。
- 门槛：RPO≤5 分钟、RTO≤30 分钟，均通过。

原始可复算记录见 `Docs/Evidence/GATE-P3/db04-restore-drill-20260810/raw-powershell-transcript.txt`。DB-04 不替代 DB-05 的生产保存间隔与最坏崩溃点故障注入，也不实现 DB-06 的 MySQL 迁移。
