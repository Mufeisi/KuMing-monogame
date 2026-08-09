# DB-03 验证证据

## 结论

DB-03 退出条件已满足：运行中的 SQLite WAL 数据库通过在线 Backup API 生成一致副本；本地与异地 `.partial` 文件均在 `integrity_check=ok` 后原子发布；自动首备、周期调度、受管文件保留、状态持久化和管理端点已接入正式宿主。正式服 SQLite 强制启用备份，异地目录必须是 UNC 路径或不同卷，且本地状态目录和异地目录在进入 Ready 前均实际验证可写。当前 Windows 验证环境存在 `C:`、`D:` 两个就绪文件系统卷，专项测试真实完成 `C:`→`D:` 副本复制和读取。GATE-P3 尚未完成，DB-04～06 继续推进。

## 命令与结果

1. DB-03 专项及关联安全测试：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SqliteBackupServiceTests|FullyQualifiedName~SqliteBackupAdminTests|FullyQualifiedName~ProductionSecurityTests|FullyQualifiedName~AdminSecurityTests" --logger "trx;LogFileName=db03-targeted.trx" --results-directory TestResults/DB03-targeted`

   结果：22/22 通过，0 失败，0 跳过。

2. Base05 全量：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-build --logger "trx;LogFileName=db03-base05-full.trx" --results-directory TestResults/DB03-base05-full`

   结果：281/281 通过，0 失败，0 跳过。

3. 服务库 Release 构建：`dotnet build Server/Server.Library.csproj -c Release --no-restore`，0 错误，2 条仓库既有包漏洞警告。

4. 窗体宿主 Release 构建：最终执行 `dotnet build Server.MirForms/Server.csproj -c Release --no-restore`，0 错误、451 条仓库既有包漏洞/可空性/线程分析警告。

5. 补丁格式：`git diff --check`，通过。

## 证据文件

- `db03-targeted.trx`：DB-03 专项及关联安全测试 22/22。
- `db03-base05-full.trx`：Base05 全量测试 281/281。

## 已知非本任务项

DB-04 承担空环境恢复、强制停止恢复、RPO/RTO 计时与每版本真实演练；DB-05 承担 `SaveDelay` 生产强校验。本任务不把测试读取副本夸大为完整运维恢复演练。
