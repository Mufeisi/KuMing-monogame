# LFENV-05 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：P0 人物、地图、行会、货币、服务器时间与装备常量；真实 NPC、条件动作、系统触发及 ScriptApi 接入

## 工件摘要

- `LingFengP0ServerSymbols` 提供 82 个 P0 规范名及 `CLASS/MAPNAME/X_COORD/Y_COORD/CREDIT/ARMOUR/BRACELET_L/BRACELET_R/AMULET/STONE` 等旧名称别名。
- `NPCSegment` 仅在翎风兼容模式接入统一 Renderer；非翎风模式继续执行旧分支，旧 `DATE` 格式不变。
- NPC `#SAY` 每行建立一次只读快照；`#IF/#ACT` 参数、系统触发页和 `ScriptApi.ResolveLegacyToken` 复用同一解析链。
- 非法语法、资源限额和快照故障整行原子保留；未支持常量保留原文并产生脱敏诊断。
- 当前模型缺少独立字段的游戏点、金刚石、灵符、灵玉、荣誉、假人计数、盾牌和时装槽位显示 `0` 或“空”，同时返回 `CompatibilitySubstitute`，不冒充真实领域数据。
- 唯一事实清单中 159 行升级为 B，运行时覆盖 82 个唯一 P0 规范名。直接常量调用覆盖基线为 `61,816 / 108,296 = 57.08%`；80% 为 LFENV-06 至 LFENV-08 的统一项目门禁。

## 自动化验证

先执行显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LingFengP0ServerSymbolIntegrationTests|FullyQualifiedName~LingFengTxtSpecialTriggerIntegrationTests|FullyQualifiedName~ServerSymbolResolver|FullyQualifiedName~ScriptTextRenderer|FullyQualifiedName~LingFengEnvirCorpusCatalogTests" --results-directory Docs/Evidence/LFENV-05-20260816 --logger "trx;LogFileName=lfenv05-targeted.trx"
```

专项结果：76/76 通过，0 失败，0 跳过。TRX：`lfenv05-targeted.trx`。

随后执行 Base05 全量回归：768/768 通过，0 失败，0 跳过，用时 2 分 13 秒。TRX：`lfenv05-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录和绝对工作区路径；XML 重新读取后计数与测试结果不变。

## 审查结论

- Spec 复核：无 BLOCKER；确认 82 个规范名、别名清单、兼容替代语义及真实入口覆盖与规格一致。
- Standards 复核：无 BLOCKER；确认原子回退、异常隔离、兼容开关、旧路径和诊断边界符合工程规范。

## SHA-256

- `lfenv05-targeted.trx`：`5F9A10F26FF0EE159E2AF8AEEC832ECE1DCF5DAB5C979043EF22381FEAA2D3FD`
- `lfenv05-full.trx`：`E5185A96A271058C952165485AD13F9D32F1B350A5C1A717C8F6A64E9EBF950A`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改本模块或专项测试，必须重跑并刷新证据。
