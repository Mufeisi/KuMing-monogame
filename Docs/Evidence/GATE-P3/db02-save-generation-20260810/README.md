# DB-02 验证证据

## 结论

DB-02 退出条件已满足：SQLite 保存使用脱离游戏可变状态的独占快照，捕获后只由专用写线程提交；保存代次单调递增并按数据域拒绝迟到旧代；成功代次只在事务成功后推进；正式连接默认 `synchronous=FULL`；主线程快照捕获与后台事务提交耗时可分别度量。GATE-P3 尚未完成，DB-03～06 仍按依赖顺序推进。

## 命令与结果

1. DB-02 专项与关联持久化测试：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Db02SaveGenerationTests|FullyQualifiedName~SqliteSingleWriterTests|FullyQualifiedName~SqlPersistenceRoundTripTests" --logger "trx;LogFileName=db02-targeted.trx" --results-directory TestResults/DB02-targeted`

   结果：11/11 通过，0 失败，0 跳过。

2. Base05 全量：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-build --logger "trx;LogFileName=db02-base05-full.trx" --results-directory TestResults/DB02-base05-full`

   结果：268/268 通过，0 失败，0 跳过。

3. 服务库 Release 构建：

   `dotnet build Server/Server.Library.csproj -c Release --no-restore`

   结果：0 错误；存在 2 条仓库既有包漏洞警告。

4. 窗体服务宿主 Release 构建：

   `dotnet build Server.MirForms/Server.csproj -c Release`

   结果：0 错误；首次在隔离工作树执行时完成 NuGet 还原，最终增量构建存在 451 条仓库既有包漏洞、可空性与线程分析警告。

5. 补丁格式：`git diff --check`，通过。

## 证据文件

- `db02-targeted.trx`：专项 11/11。
- `db02-base05-full.trx`：全量 268/268。

## 已知非本任务项

构建中的既有包漏洞、可空性和线程分析警告不在 DB-02 范围。在线备份/恢复、生产 RPO 强校验与 MySQL 切换门槛分别留在 DB-03～DB-06。
