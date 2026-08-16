# LFENV-07 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：P2 英雄、宝宝与宠物服务器常量

## 工件摘要

- P2 运行时目录锁定 68 个唯一规范名；事实清单新增或更新 109 行 B、48 行 E，缺少独立领域模型的英雄成长、英雄物品上下文和宠物 MP/伤害项保持 E，不以空值冒充兼容。
- 英雄身份、在线镜像、装备、目标、受击、技能与击杀常量按可用模型分层；未召唤英雄仅返回静态身份和装备，不伪造在线 HP、地图或坐标。
- `SLAVE*`、`PET.*` 从人物宠物和英雄宠物集合按稳定顺序合并、引用去重，并过滤死亡或已离开世界的对象。
- 最终伤害来源以枚举和拥有者名称值快照进入事件上下文，不保留领域对象引用；人物、英雄、人物宠物、英雄召唤宝宝和派生怪直接扣血入口均有真实链覆盖。
- 旧伤害 Hook 构造器、事件记录构造器和 4/13 参数 `Deconstruct` 保持兼容。
- 真实语料中的直接 B/C 覆盖提升至 `88,896 / 108,296 = 82.09%`，达到 GATE-C0 的 80% 门槛；E/X 不计入兼容覆盖。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LingFengP2ServerSymbolIntegrationTests|FullyQualifiedName~LingFengP1ServerSymbolIntegrationTests|FullyQualifiedName~LingFengP0ServerSymbolIntegrationTests|FullyQualifiedName~LingFengTxtSpecialTriggerIntegrationTests|FullyQualifiedName~ServerSymbolResolver|FullyQualifiedName~ScriptTextRenderer|FullyQualifiedName~LingFengEnvirCorpusCatalogTests" --results-directory Docs/Evidence/LFENV-07-20260816 --logger "trx;LogFileName=lfenv07-targeted.trx"
```

专项结果：96/96 通过，0 失败，0 跳过。TRX：`lfenv07-targeted.trx`。

随后执行 Base05 全量回归：788/788 通过，0 失败，0 跳过，用时 2 分 14 秒。TRX：`lfenv07-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。

## 审查结论

- Spec 复核：无 BLOCKER；确认最终伤害拥有者值快照、跨拥有者击杀隔离、H.MP 事实清单补录和 68 个唯一 P2 规范名符合规格。
- Standards 复核：无 BLOCKER；确认英雄召唤宝宝归属、宠物稳定选择、派生怪集中伤害入口以及旧构造/解构兼容符合工程规范。
- 非阻断：`LingFengP0ServerSymbols` 已承载 P0/P1/P2 目录与绑定，后续可在不改变公开契约的阶段提取；本阶段不扩大重构范围。

## SHA-256

- `lfenv07-targeted.trx`：`BDA5A842613C13B5612B658F44F389626A2411CFDCEE08F482E8B79E842E6D83`
- `lfenv07-full.trx`：`69C731DEC043A8B0D00B6DFF65D68925D8AE6C46B8B6851F8733F35C2A0D2842`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改本模块或专项测试，必须重跑并刷新证据。
