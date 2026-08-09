# DB-04 验证证据

## 结论

当前版本已完成空环境与强制停止两条真实恢复演练。副本由与 DB-03 相同的 Microsoft.Data.Sqlite `BackupDatabase` API 从 WAL 源库生成；正式 `Server.dll` 命令对备份和目标 `.partial` 分别执行完整性检查，原子发布新库并保留旧主库/WAL/SHM 回滚组。强停演练 RPO 2.224 秒、完整 RTO 1157ms，满足 RPO≤5 分钟和 RTO≤30 分钟。DB-05 仍需完成生产保存间隔强校验及故障注入，GATE-P3 尚未关闭。

## 自动化与构建

1. DB-04 专项：`dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~SqliteRestoreServiceTests"`。
   结果：6/6 通过，0 失败，0 跳过。
2. Base05 全量：288/288 通过，0 失败，0 跳过。
3. `dotnet build Server/Server.Library.csproj -c Release --no-restore`：0 错误，2 条仓库既有包漏洞警告。
4. `dotnet build Server.MirForms/Server.csproj -c Release --no-restore`：0 错误，4 条仓库既有包漏洞警告。
5. 未知 CLI 命令返回退出码 `2` 并输出中文用法。
6. `git diff --check`：通过。

证据文件：`db04-targeted.trx` 为专项 6/6；`db04-base05-full.trx` 为全量 288/288。

## 真实进程演练

准备程序只在 `TestResults` 中生成一次性 SQLite 数据，不纳入产品或提交。正式被测入口为 Release 构建的 `Server.dll --restore-sqlite`。

- 空环境：在线 Backup API 产物作为来源，退出码 0，恢复后读取值 `20260810`，从恢复启动到读取验证完成 1161ms。
- 强停：另一个 WAL 事务进程被强制终止，确认 WAL/SHM 存在；正式入口从在线 Backup API 产物恢复，退出码 0，恢复后读取值 `20260810`；从终止到读取验证完成 1157ms，备份年龄 2.224 秒。
- 原始输出摘要见 `cli-drill.txt`。

## 阶段边界

本记录只证明当前版本完成一次真实演练。每个后续拟发布版本仍需按 `Docs/DB-04-SQLite恢复演练.md` 重新归档。DB-05 的 1～5 分钟生产配置强校验和故障注入不在本任务内。
