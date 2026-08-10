# OPS-BASIC-02 验证证据

## 任务简报

- 目标：PC、Android 与服务端崩溃后留下可直接取回的离线诊断包。
- 做：复用现有日志和资源版本状态，收集最后日志片段、程序版本、资源版本、白名单配置摘要与异常；接入三端正式异常边界。
- 不做：自动上传、完整线程/网络快照、转储平台、压力测试和 OPS-02 高级诊断。
- 方法约束：不重造日志、微端、协议或资源版本系统；不记录账户、口令、令牌或连接串；原子发布并限制日志大小。
- 预估时间：120～180 分钟。
- 完成定义：三端正式宿主接线可编译；诊断包内容、脱敏和失败清理有专项测试；Base05 完整回归通过。
- 语言：交流、文档、状态和提交信息均使用中文；代码标识符、命令及原始工具输出除外。

## 工件与命令

专项测试：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --filter FullyQualifiedName~CrashDiagnosticBundleTests --logger "trx;LogFileName=ops-basic-02-targeted.trx" --results-directory Docs/Evidence/GATE-P5/ops-basic-02-crash-diagnostics-20260810
```

完整测试：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --logger "trx;LogFileName=ops-basic-02-base05-full.trx" --results-directory Docs/Evidence/GATE-P5/ops-basic-02-crash-diagnostics-20260810
```

构建：

```text
dotnet build Client_VorticeDX11/Client_VorticeDX11.csproj --no-restore --nologo
dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -f net10.0 --no-restore --nologo
dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -f net10.0-android --no-restore --nologo
dotnet build Client_MonoGame.Android/Client_MonoGame.Android.csproj --no-restore --nologo
dotnet build Server/Server.Library.csproj --no-restore --nologo
dotnet build Server.MirForms/Server.csproj --no-restore --nologo
```

最终结果：

- 专项 TRX：3/3 通过，0 失败，0 跳过。
- Base05 完整 TRX：333/333 通过，0 失败，0 跳过。
- `Shared`、PC、MonoGame `net10.0`、Android arm64、`Server.Library` 与 `Server.MirForms`：均 0 错误。
- 服务端构建目录存在 `resources.manifest.json`，与仓库根清单 SHA-256 同为 `48EAE88CE21C759ABEDCF2B8E05E293C127960BC12008C5B235F0C1FD7EF53B3`。
- `git diff --check`：通过。

隔离工作树第一次完整测试为 331/332，唯一失败是 DB-05 故障注入测试要求的 Release 子进程程序集尚未生成；先构建既有 `Base05.Tests` Release 工件后，未修改业务逻辑即重跑为 332/332。代码审查随后补强首次启动资源身份、Android 日志同步排空、JSON/Basic/连接串脱敏及脱敏后 64 KiB 上限，新增 1 项回归后最终全量为 333/333。构建中的既有空值、分析器、`WindowsBase` 冲突以及 NuGet 漏洞警告不在本任务范围；依赖漏洞统一留在同阶段 OPS-BASIC-04 扫描与处置。

## 每日工件检查

- 代码、三端正式接线、测试、TRX、构建输出和运维说明均为可直接使用或审计的工件。
- 没有创建分析器、矩阵工具、上传服务或新测试框架；过程资产少于工件数量。
- 语言检查：中文文档与中文提交信息；代码标识符、命令和工具原始输出保持原文。
