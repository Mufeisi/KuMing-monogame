# DB-03 验证证据

## 结论

DB-03 退出条件已满足：运行中的 SQLite WAL 数据库通过在线 Backup API 生成一致副本；本地与异地 `.partial` 文件均在 `integrity_check=ok` 后原子发布；自动首备、周期调度、受管文件保留、状态持久化和管理端点已接入正式宿主。正式服 SQLite 强制启用备份并要求异地目录。GATE-P3 尚未完成，DB-04～06 继续推进。

## 命令与结果

1. DB-03 专项及关联安全测试：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SqliteBackupServiceTests|FullyQualifiedName~SqliteBackupAdminTests|FullyQualifiedName~ProductionSecurityTests|FullyQualifiedName~AdminSecurityTests" --logger "trx;LogFileName=db03-targeted.trx" --results-directory TestResults/DB03-targeted`

   结果：19/19 通过，0 失败，0 跳过。

2. Base05 全量：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-build --logger "trx;LogFileName=db03-base05-full.trx" --results-directory TestResults/DB03-base05-full`

   结果：278/278 通过，0 失败，0 跳过。

3. 服务库 Release 构建：`dotnet build Server/Server.Library.csproj -c Release --no-restore`，0 错误，2 条仓库既有包漏洞警告。

4. 窗体宿主 Release 构建：首次执行 `dotnet build Server.MirForms/Server.csproj -c Release` 完成 NuGet 还原，0 错误、455 条仓库既有包漏洞/可空性/线程分析警告；最终执行 `--no-restore` 增量复验，0 错误、4 条既有包漏洞警告。

5. 补丁格式：`git diff --check`，通过。

## 证据文件

- `db03-targeted.trx`：DB-03 专项及关联安全测试 19/19。
- `db03-base05-full.trx`：Base05 全量测试 278/278。

## 已知非本任务项

DB-04 承担空环境恢复、强制停止恢复、RPO/RTO 计时与每版本真实演练；DB-05 承担 `SaveDelay` 生产强校验。本任务不把测试读取副本夸大为完整运维恢复演练。
