# DB-01 验证证据

## 结论

DB-01 退出条件已满足：SQLite 正式连接启用 WAL 与 5 秒忙等待；运行期保存由唯一后台写线程串行提交，同域待处理请求合并为最新快照；关服在主线程捕获最终快照、排空队列，并在连续保存失败时取消关服。GATE-P3 尚未完成，DB-02～06 仍按依赖顺序推进。

## 命令与结果

1. DB-01 专项与关联持久化测试：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SqliteSingleWriterTests|FullyQualifiedName~SqlPersistenceRoundTripTests|FullyQualifiedName~Sqlite关服|FullyQualifiedName~Sqlite最终保存" --logger "trx;LogFileName=db01-targeted-pass.trx" --results-directory TestResults/DB01-targeted-pass`

   结果：12/12 通过，0 失败，0 跳过。

2. Base05 全量：

   `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-build --logger "trx;LogFileName=db01-base05-full.trx" --results-directory TestResults/DB01-base05-full`

   结果：265/265 通过，0 失败，0 跳过。

3. 服务库 Release 构建：

   `dotnet build Server/Server.Library.csproj -c Release --no-restore`

   结果：0 错误；存在仓库既有包漏洞与编译警告。

4. 窗体服务宿主 Release 构建：

   `dotnet build Server.MirForms/Server.csproj -c Release`

   结果：0 错误；首次在隔离工作树执行时完成 NuGet 还原，存在仓库既有包漏洞、可空性与线程分析警告。

5. 补丁格式：`git diff --check`，通过。

## 证据文件

- `db01-targeted-pass.trx`：专项 12/12。
- `db01-base05-full.trx`：全量 265/265。

## 已知非本任务项

构建仍报告 `log4net 3.0.3` 与 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的已知漏洞警告；依赖升级不属于 DB-01，保持为后续依赖治理项。DB-02 的保存代次、`synchronous=FULL` 和完整耗时口径未在本任务提前实现。
