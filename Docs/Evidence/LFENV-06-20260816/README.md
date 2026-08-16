# LFENV-06 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：P1 击杀、物品、战斗目标、脚本参数、队伍、召回与活动倍率服务器常量

## 工件摘要

- P1 运行时目录锁定 53 个规范名；事实清单中 84 行关联 `LFENV06-P1`，其中 B 64 行、C 20 行。
- 击杀、拾取、使用、掉落和伤害入口立即复制不可变事件快照；线程内显式栈支持嵌套恢复，事件退出后不保留领域对象引用。
- `SCRIPTPARAM1...9` 使用不可写参数副本；`TEAM0...9` 按当前队伍稳定顺序读取，越界保留原占位符。
- 普通攻击及未触发被动技能的 `CURRRUSEMAGICID` 为 `0`；实际生效技能随 `DelayedAction` 进入 `CompleteAttack`，双龙斩/双斩的附加延迟伤害也携带同一技能标识。
- 真实领域测试覆盖非零击杀坐标与经验、拾取金币、使用物品栏位置、人物受击、普通/技能攻击以及多物品掉落取首件。
- 当前模型缺少独立攻人/攻怪倍率，相关常量以 `1/0` 返回并产生 `CompatibilitySubstitute`；百分比倍率仅折算完整整数倍，聚合掉落只显示首件。这些条目保持 C，不冒充完整兼容。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LingFengP1ServerSymbolIntegrationTests|FullyQualifiedName~LingFengP0ServerSymbolIntegrationTests|FullyQualifiedName~LingFengTxtSpecialTriggerIntegrationTests|FullyQualifiedName~ServerSymbolResolver|FullyQualifiedName~ScriptTextRenderer|FullyQualifiedName~LingFengEnvirCorpusCatalogTests" --results-directory Docs/Evidence/LFENV-06-20260816 --logger "trx;LogFileName=lfenv06-targeted.trx"
```

专项结果：88/88 通过，0 失败，0 跳过。TRX：`lfenv06-targeted.trx`。

随后执行 Base05 全量回归：780/780 通过，0 失败，0 跳过，用时 2 分 20 秒。TRX：`lfenv06-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。

## 审查结论

- Spec 复核：无 BLOCKER；确认普通攻击不串入未触发被动技能、真实技能延迟链、非零击杀快照和多物品首件语义符合规格。
- Standards 复核：无 BLOCKER；确认不可写参数快照、主线程旁路 Hook、延迟动作兼容、ThreadStatic 嵌套恢复和真实接口测试符合工程规范。
- 非阻断：`LingFengP0ServerSymbols` 已同时承载 P0/P1，后续阶段可按模块边界重命名或提取；本阶段不为此扩大改动范围。

## SHA-256

- `lfenv06-targeted.trx`：`B49648ECC34EAA1D2AD9D35B95F9B1B00B63E80904451732869526F7E551247D`
- `lfenv06-full.trx`：`C6A56C34247E6CB39033664246E8B78C032A6BF6CAD613786C359EFDC4D28271`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改本模块或专项测试，必须重跑并刷新证据。
