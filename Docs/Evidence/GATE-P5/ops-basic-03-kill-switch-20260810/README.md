# OPS-BASIC-03 Kill Switch 验证证据

## 工件

- `ops-basic-03-targeted.trx`：专项 8/8，通过。
- `base05-full.trx`：Base05 全量 341/341，通过。
- `server-library-build.txt`：`Server/Server.Library.csproj -c Release`，0 错误。
- `server-mirforms-build.txt`：`Server.MirForms/Server.csproj -c Release`，0 错误。

## 执行命令

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --filter FullyQualifiedName~KillSwitchServiceTests --logger "trx;LogFileName=<evidence>\ops-basic-03-targeted.trx"
dotnet build Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --logger "trx;LogFileName=<evidence>\base05-full.trx"
dotnet build Server/Server.Library.csproj -c Release --no-restore
dotnet build Server.MirForms/Server.csproj -c Release
git check-ignore -v --no-index Configs/Operations/kill-switches.json Configs/Operations/kill-switches.json.partial-example
git diff --check
```

首次全量运行时，DB-05 的强停子进程用例因新工作树尚无 `bin/Release` 测试程序集而提前退出；生成 Release 测试宿主后按相同代码重跑；审查修复加入可靠审计与状态重放故障注入后，最终全量 341/341 通过。该前置不涉及产品代码改动。

`git check-ignore` 分别命中 `.gitignore` 的正式状态与半成品规则。构建中的 NuGet 安全告警和既有可空性/线程分析告警不作为本任务处理；依赖漏洞与许可证收口属于紧随其后的 OPS-BASIC-04，两个正式服务端项目均为 0 错误。

## 验证结论

真实管理 HTTP 已验证 Operator 查询、Operator 修改拒绝、Administrator 修改和状态 JSON；关闭更新后，经过 Micro 用户/代码鉴权的真实 `/api/file` 请求返回 `503`，健康检查继续可用。直接业务入口验证商城不再读取商品、魔龙经验不再推进、开户返回关闭结果。持久化复验覆盖重启保持、缺字段、未知版本和损坏 JSON 失败关闭；完整审计与状态原子保存并在启动时重放核对四闸，运行日志故障不会伪报开关失败，检索日志不包含原因原文。
