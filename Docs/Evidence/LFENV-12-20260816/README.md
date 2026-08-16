# LFENV-12 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：`MapInfo`、`Mongen`、`MapQuest` 世界内容 Provider 与真实冷启动、地图切换、刷怪和怪物死亡任务链

## 工件摘要

- 根目录 `MapInfo.txt`、`Mongen.txt` 和可选 `MapQuest.txt` 构建为单个不可变世界候选，不进入 NPC 命令解释器。
- 地图别名、属性、传送、刷怪、人物 Flag、怪物和任务页面依赖在地图创建前一次性验证；任一失败都不产生部分运行态。
- 真实地图查找和移动同时接受物理地图名与翎风别名；`Mongen` 进入既有 `RespawnInfo/MapRespawn/MonsterObject.Spawn` 链。
- `MapQuest` 在真实怪物死亡时按最终经验归属、地图、人物 Flag 和怪物匹配，并在一次主线程操作内经既有重入预算、异常隔离和命令门禁派发一次。
- 运行中的结构热更失败关闭并保留上一完整候选；页面正文可按普通 TXT 快照热更。
- Legacy 二进制保存和 SQL 世界关系保存只写原数据库基线，不把物理 Envir 覆盖层写回数据库。
- `LFENV-ROOT-0002` 代表 Envir 已通过严格物理候选构建。目标服真实数据库依赖的全根计划构建与多版本差分仍按规格留给 LFENV-15/18，不把本阶段结果冒充 GATE-ALL。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --filter "FullyQualifiedName~LingFengWorldContentProviderTests|FullyQualifiedName~RepresentativeEnvir_StrictlyBuildsMonsterDomainCandidate" --logger "trx;LogFileName=lfenv12-targeted.trx" --results-directory Docs/Evidence/LFENV-12-20260816
```

专项结果：15/15 通过，0 失败，0 跳过。TRX：`lfenv12-targeted.trx`。

随后执行 Base05 全量回归：854/854 通过，0 失败，0 跳过，用时 2 分 20 秒。TRX：`lfenv12-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录、外部语料根和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。目录内 `.gitattributes` 固定 TRX 为 CRLF，保证干净检出后的哈希稳定。

## 审查结论

- Spec 复核：无 BLOCKER；已关闭负坐标、Flag 越界、物理地图名任务匹配、悬空重连目标和真实移动链覆盖等问题。
- Standards 复核：无 BLOCKER；已关闭同物理地图别名冲突、怪物线程读取人物 Flag、测试全局运行态恢复和任务语义重复等问题。

## SHA-256

- `lfenv12-targeted.trx`：`90DA91184CD940385F0C946369DBA6DA553B748C0F6C9B78B9B53DCA6C73BC7F`
- `lfenv12-full.trx`：`99860C3A588DAB8EE8DB78459C476BDA46AE79510FFF1BF5E4C2D18694F90E47`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改世界 Provider、地图持久化、地图任务派发或专项测试，必须重跑并刷新证据。
