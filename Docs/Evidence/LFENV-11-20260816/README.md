# LFENV-11 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：`MonItems`、`MonUseItems`、`SmartMonster` 领域 Provider 与真实怪物属性/死亡掉落链

## 工件摘要

- `MonItems` 映射到既有 `IDropTableProvider`，支持物品、金币、随机/首个命中组、嵌套组、`QuestDiary #CALL` 和类型 7 变量条件。
- 条件比较与 `QFunction-0` 回调在同一个主线程操作内执行；比较失败、回调失败、重入、异常或预算失败均不会产生该组掉落。
- `MonUseItems` 严格解析怪物选项、装备和技能元数据；装备基础属性及掉装配置进入真实 `Die()` 链，并与空操作 C# `MonsterDropBefore` Hook 共存。
- `SmartMonster` 以不可变配置快照保存，不把客户端动作、声音或旧寻路参数伪装成服务端 AI。
- 同名来源、重复字段、重复装备槽、重复技能字段、缺失引用、循环、非法括号和物品依赖均在发布前失败关闭；热更失败保留上一完整文本、变量、怪物内容和掉落快照。
- 活怪在下一次处理或掉落前同步切换属性与掉落快照，避免同一次行为混用两版内容。
- `LFENV-ROOT-0002` 代表 Envir 已通过严格领域候选构建。其他版本中已观察到的空概率分母、缺括号等原始坏行不做猜测修复，继续进入 LFENV-15/18 的阻断清单与多版本差分。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LingFengMonsterDropProviderTests|FullyQualifiedName~DropContentAuthoringTests|FullyQualifiedName~PhysicalTextFileProviderTests|FullyQualifiedName~LingFengEnvirFileClassifierTests|FullyQualifiedName~TxtScriptReloadCoordinatorTests|FullyQualifiedName~LingFengTxtSpecialTriggerIntegrationTests|FullyQualifiedName~LingFengTxtSystemHookAdapterTests" --results-directory Docs/Evidence/LFENV-11-20260816 --logger "trx;LogFileName=lfenv11-targeted.trx"
```

专项结果：92/92 通过，0 失败，0 跳过。TRX：`lfenv11-targeted.trx`。

随后执行 Base05 全量回归：840/840 通过，0 失败，0 跳过，用时 2 分 34 秒。TRX：`lfenv11-full.trx`。

说明书使用 `requirements.lock.txt` 的隔离 Python 环境执行 `Build-Manual.ps1` 严格构建，构建成功且搜索索引生成完成。

两份提交版 TRX 已脱敏用户名、设备名、用户目录、外部语料根和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。

## 审查结论

- Spec 复核：无 BLOCKER；审查发现并关闭了 C# Before 空处理丢装备掉落、类型 7 回调未执行和掉落线程读取变量模块的问题。
- Standards 复核：无 BLOCKER；审查发现并关闭了重复内容覆盖、非原子内容发布、活怪属性未刷新、测试全局状态并行污染和重复键覆盖问题。

## SHA-256

- `lfenv11-targeted.trx`：`FE9EE0202A4577B34429521AC6A6543EFFB5D80666ECDCE523B269075F4C4A79`
- `lfenv11-full.trx`：`72AF4FE3D432E22A3E5E8658E13EB93F415B665D2FC2491879BF957F5DCCCDAB`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改怪物领域 Provider、掉落 Hook、系统回调、内容热更或专项测试，必须重跑并刷新证据。
