# LFENV-10 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：翎风 QManage/QFunction 既有系统入口与 AutoRunRobot/RobotManage 定时调度

## 工件摘要

- `Robot_def/AUTORUNROBOT.TXT` 发布为 `SystemScripts/AutoRunRobot`，支持 `SEC/MIN/HOUR/RUNONDAY/RUNONWEEK`，间隔、条目数和单 tick 执行数均有硬上限。
- `Robot_def/ROBOTMANAGE.TXT` 发布为 `SystemScripts/RobotManage`，翎风模式加载全部页面；AutoRun 缺少 RobotManage 或精确目标标签时，候选在发布前失败关闭。
- 调度运行在既有服务器主循环，支持发布当秒固定任务、周期任务、异常隔离、重入拒绝和预算计数；停止、禁用或重载时清除新旧 Robot 状态。
- 热更新语法、编码、目标标签或其他快照验证失败时保留上一成功 Provider 与调度版本，不发布半成品。
- 24 个版本家族代表样本的实际 `AUTORUNROBOT.TXT` 已通过严格语法解析；参考服自身存在的 AutoRun/RobotManage 标签错配不做模糊纠正，将由 LFENV-18 全语料门禁按内容依赖明确阻断。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LingFengRobotScheduleProviderTests|FullyQualifiedName~LingFengEnvirFileClassifierTests|FullyQualifiedName~PhysicalTextFileProviderTests|FullyQualifiedName~TxtScriptReloadCoordinatorTests|FullyQualifiedName~LingFengTxtSystemHookAdapterTests|FullyQualifiedName~LingFengTxtSpecialTriggerIntegrationTests|FullyQualifiedName~ServerLifecycleSmokeTests" --results-directory Docs/Evidence/LFENV-10-20260816 --logger "trx;LogFileName=lfenv10-targeted.trx"
```

专项结果：86/86 通过，0 失败，0 跳过。TRX：`lfenv10-targeted.trx`。

随后执行 Base05 全量回归：822/822 通过，0 失败，0 跳过，用时 2 分 13 秒。TRX：`lfenv10-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。

## 审查结论

- Spec 复核：无 BLOCKER；审查发现并关闭了真实服务器时钟带毫秒时固定任务错过发布当秒的问题。
- Standards 复核：无 BLOCKER；审查发现并关闭了 AutoRun 缺 RobotManage 或目标标签仍可发布并静默不执行的问题。

## SHA-256

- `lfenv10-targeted.trx`：`B0BCCD120EF297F8B2B0B62CC6CA76A5822DE80CD3DF7386E651CC8FD7BF569D`
- `lfenv10-full.trx`：`33D273AAB9683A94166C7A44DD446E4F56BBFF9DA4030BDA8E3BAD23AA0CBFA0`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改调度器、Robot 领域入口、候选验证器或专项测试，必须重跑并刷新证据。
