# LFENV-09 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：翎风 Envir 文件唯一归属与脚本发布边界

## 工件摘要

- 新增 `LingFengEnvirFileClassifier` 和 11 条有序所有权规则，文件唯一归入脚本、领域配置、运行数据、客户端契约、备份归档、文档附件、可执行工件或未归属。
- 只有 `Script` 所有者可进入 `PhysicalTextFileProvider`；运行数据、领域配置、客户端契约、备份、文档和可执行工件均不会被 TXT 热更新覆盖。
- 未归属、非法路径和非法脚本逻辑 Key 在候选构造阶段失败关闭；LyoCrystal 原布局继续使用既有 `*.txt` 和目录映射，不受翎风分类规则影响。
- `QFunction-0` 在根目录和 `Market_Def` 下均映射到 `SystemScripts/QFunction-0`；两者并存时确定选择标准 `Market_Def`，仅根级存在时兼容回退，其他重复 Key 继续拒绝。
- 24 个版本家族代表 Envir 已逐文件真实扫描，所有可见、非系统、非重解析点文件未归属为零；所有权事实清单为 `Docs/generated/scripting/lingfeng-envir-file-ownership.csv`。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LingFengEnvirFileClassifierTests|FullyQualifiedName~PhysicalTextFileProviderTests|FullyQualifiedName~TxtScriptReloadCoordinatorTests|FullyQualifiedName~LingFengTxtSystemHookAdapterTests|FullyQualifiedName~LingFengTxtSpecialTriggerIntegrationTests|FullyQualifiedName~ServerLifecycleSmokeTests" --results-directory Docs/Evidence/LFENV-09-20260816 --logger "trx;LogFileName=lfenv09-targeted.trx"
```

专项结果：78/78 通过，0 失败，0 跳过。TRX：`lfenv09-targeted.trx`。

随后执行 Base05 全量回归：814/814 通过，0 失败，0 跳过，用时 2 分 15 秒。TRX：`lfenv09-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。

## 审查结论

- Spec 复核：无 BLOCKER；确认 QFunction 系统入口、根级回退和代表样本正确归属符合规格。
- Standards 复核：无 BLOCKER；确认别名冲突消解范围受控、枚举顺序无关，其他重复 Key 和未知文件仍失败关闭。

## SHA-256

- `lfenv09-targeted.trx`：`FEF0A301CE6F8DB2BCE1FBD24BEF9BD805A2F0159427078FBB2B9DDA369FCF1D`
- `lfenv09-full.trx`：`A9D0093BEBCDDBBC96C574EFEE640D8AC2A56E545D329BB2F1C7C3E6616226C5`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改分类器、Provider 或专项测试，必须重跑并刷新证据。
