# OPS-BASIC-01 验证证据

## 任务简报

- 目标：交付可直接读取的服务端核心指标 JSON 和可验证日志告警。
- 做：复用 PERF-00、DB-03、SEC-04，接入正式服务端生命周期，增加专项测试与运维说明。
- 不做：压力测试、高级仪表盘、崩溃包、Kill Switch、自动上报和发布流水线。
- 方法约束：不造第二套指标；不跨线程读取/修改玩家对象；不改协议、渲染、脚本、Schema 或微端接缝。
- 预估时间：90～150 分钟。
- 完成定义：Operator 可经真实 HTTP 读取在线/Tick p95/保存耗时/队列/备份；告警触发与恢复可验证；专项、全量和两项服务端构建通过。
- 语言：交流、文档、状态和提交信息均使用中文；代码标识符及原始命令除外。

## 工件与命令

专项测试：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~BasicOperations" --logger "trx;LogFileName=ops-basic-01-targeted.trx" --results-directory Docs/Evidence/GATE-P5/ops-basic-01-monitoring-20260810
```

完整测试：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --logger "trx;LogFileName=ops-basic-01-base05-full.trx" --results-directory Docs/Evidence/GATE-P5/ops-basic-01-monitoring-20260810
```

构建：

```text
dotnet build Server/Server.Library.csproj -c Release --no-restore --nologo
dotnet build Server.MirForms/Server.csproj -c Release --no-restore --nologo
```

最终结果：

- 专项 TRX：8/8 通过，0 失败，0 跳过。
- Base05 完整 TRX：330/330 通过，0 失败，0 跳过。
- `Server.Library` Release：10 个既有警告，0 错误。
- `Server.MirForms` Release：451 个既有警告，0 错误。
- `git diff --check`：通过。

新工作树第一次完整测试因 DB-05 子进程所需的 Release 测试程序集尚未生成而出现 1 项环境失败，其余 327 项通过；构建既有 Release 子进程工件后继续完成代码审查修复，最终归档 330/330 结果。NuGet 还原报告的既有 `log4net` 与 `SQLitePCLRaw` 漏洞不在 OPS-BASIC-01 范围，纳入同阶段 OPS-BASIC-04 依赖漏洞扫描处理，不在本任务越界升级依赖。

## 每日工件检查

- 代码/测试/运行证据/运维文档均为用户可直接使用或审计的工件。
- 过程资产少于工件数量，没有创建分析器、矩阵工具或新测试框架。
- 语言检查：中文文档与中文提交信息；原始命令、标识符和工具警告保持原文。
