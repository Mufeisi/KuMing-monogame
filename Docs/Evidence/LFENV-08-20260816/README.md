# LFENV-08 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：P3 行会、攻城与公开服务器状态常量

## 工件摘要

- P3 运行时目录接入 9 个唯一规范名：`CASTLEGOLD`、`CASTLENAME`、`CASTLEWARDATE`、`GUILDMASTER1`、`GUILDMASTER2`、`GUILDWARFEE`、`LISTOFWAR`、`OWNERGUILD`、`REQUESTBUILDGUILDITEM`。
- 人物行会、城堡占领者、城堡资金和公开服务器配置均以只读值快照解析；`CASTLEWARDATE` 与 `LISTOFWAR` 因当前模型缺少持久化申请日期和翎风原生排版，以 C/`CompatibilitySubstitute` 明示返回。
- 本次 NPC 调用 ID 显式进入统一渲染 seam，可区分“未提供调用上下文”和“本次普通 NPC 无城堡”，不会读取残留 `player.NPCObjectID`；真实 TXT 页面、C# 页面和 `ScriptApi` 均有回归覆盖。
- 事实目录新增 LFENV08-P3、LFENV08-E、LFENV08-X 契约：缺少领域来源的高级系统项保持 E，银行账号、手机号和 QQ 等敏感项保持 X，不以空值冒充兼容。
- 真实语料中的直接 B/C 覆盖提升至 `89,169 / 108,296 = 82.34%`，继续满足 GATE-C0 的 80% 门槛；E/X 不计入兼容覆盖。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LingFengP3ServerSymbolIntegrationTests|FullyQualifiedName~LingFengP2ServerSymbolIntegrationTests|FullyQualifiedName~LingFengP1ServerSymbolIntegrationTests|FullyQualifiedName~LingFengP0ServerSymbolIntegrationTests|FullyQualifiedName~LingFengTxtSpecialTriggerIntegrationTests|FullyQualifiedName~ServerSymbolResolver|FullyQualifiedName~ScriptTextRenderer|FullyQualifiedName~LingFengEnvirCorpusCatalogTests" --results-directory Docs/Evidence/LFENV-08-20260816 --logger "trx;LogFileName=lfenv08-targeted.trx"
```

专项结果：101/101 通过，0 失败，0 跳过。TRX：`lfenv08-targeted.trx`。

随后执行 Base05 全量回归：793/793 通过，0 失败，0 跳过，用时 2 分 13 秒。TRX：`lfenv08-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。

## 审查结论

- Spec 复核：无 BLOCKER；确认 LFENV08-E/X 事实目录、P3 支持边界和真实语料覆盖率符合规格。
- Standards 复核：无 BLOCKER；确认显式 NPC 调用上下文可阻断残留会话串城，并由真实 TXT、C# 与 `ScriptApi` 调用回归覆盖。
- 非阻断：P3 Adapter 当前会为每次兼容渲染枚举少量城堡；现阶段不影响正确性，后续性能阶段可按真实基准决定是否优化。

## SHA-256

- `lfenv08-targeted.trx`：`62F0B5447C21F9380D1D070BD999B3A1827F43C90070775C9EDE39A6836B0F59`
- `lfenv08-full.trx`：`DFB056BD081A53BE3470C9850F20C2990E3043F17E5A42724BE26A4912680675`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改本模块或专项测试，必须重跑并刷新证据。
