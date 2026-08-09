# DB-04 验证证据

## 结论

当前版本已完成空环境、真实 WAL 强停、原子发布、失败回滚和正式 CLI 恢复闭环。恢复前把强停目标收敛为已验证单文件旧库，再原子发布新库，消除了移动主库/WAL/SHM 时再次中断造成跨代次的窗口。

真实演练使用 DB-03 正式备份服务生成 C: 本地与 D: 异卷副本；强停进程 PID、UTC 时间、三文件状态、SHA-256、正式 CLI 输出/退出码、五域读取值和 `Ready` 状态均在 `raw-powershell-transcript.txt` 中。可复算结果：RPO 303ms，恢复命令到 Ready 的 RTO 600ms，故障到 Ready 625ms，满足 RPO≤5 分钟、RTO≤30 分钟。

## 自动化与构建

1. DB-04 专项：`SqliteRestoreServiceTests`，11/11 通过。
2. Base05 全量：最终合并基线上 306/306 通过。
3. `Server.Library` Release：0 错误。
4. `Server.MirForms` Release：0 错误。
5. 正式 `Server.dll --restore-sqlite`：退出码 0；未知 CLI 命令测试覆盖退出码 2。
6. `git diff --check`：通过。

`db04-targeted.trx` 与 `db04-base05-full.trx` 是最终测试原始结果；两份构建日志保留完整输出。

## 真实进程演练

- 数据：非空账号 `db04-account`、角色 `DB04Character`、行会 `DB04Guild`、商品 `45001.msd`、攻城 `DB04Conquest`，均通过现有 SQL/legacy 接缝写入。
- 备份：DB-03 `SqliteBackupService.RunNow("db04-real-drill")`；本地与异地副本均为 675840 字节、SHA-256 `99BCCCC54FA82C56C8B632A3E49D3055F0A074621EF764C62FE0FB9F5FE2D69C`、完整性 `ok`。
- 强停：PID 1156 在提交 `GOLD=777` 后保持 WAL；强停前后均确认主库、4152 字节 WAL 与 32768 字节 SHM 存在。
- 恢复：正式 Release `Server.dll` 返回 0，报告恢复耗时 101ms；五域复读、完整性检查及宿主 `Ready` 完成后总 RTO 为 600ms。
- 数据损失边界：恢复后 `GOLD=100`，准确回到 303ms 前的最后成功备份；故障后的 777 不会伪装为已备份数据。

`cli-drill.txt` 是便于阅读的摘要；审计以 `raw-powershell-transcript.txt` 原始记录为准。一次性演练驱动器位于被忽略的 `TestResults`，不纳入产品或提交。

## 阶段边界

DB-04 只证明当前版本的一次真实恢复。DB-05 仍需独立证明生产自动保存最坏崩溃点 RPO；后续拟发布版本必须重新演练。
