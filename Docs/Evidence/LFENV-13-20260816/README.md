# LFENV-13 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：`Shopitemlist.txt`、`Makeitem.txt`、根规则名单、双运行时优先级、热更回滚与真实商城/合成事务

## 工件摘要

- `LingFengCommerceContentProvider` 严格解析商城和配方，`LingFengRuleListContentProvider` 独立解析名单；二者复用现有 `GameShopItem/IRecipeProvider/INameListProvider`，没有建立万能 Envir Provider。
- 商城十列记录、配方节和材料、名单逻辑 Key 及物品依赖在发布前一次性验证；错误候选保留上一完整文本、商城、配方与名单快照。
- 物理商城覆盖层不写回数据库 `GameShopList` 或 SQL 保存视图；真实购买链覆盖余额不足无副作用、非堆叠多件交付、单人库存、扣款、邮件和审计键一致。
- 真实 `Market_Def` NPC `[RECIPE]` 链覆盖物理 `Makeitem.txt` 合成；背包越界槽位不消耗材料，合法槽位只生成一次成品。
- C#/TXT 配方和名单遵循既有优先级与回落开关；只修改商业领域文件也会改变热更摘要并报告对应 `ChangedKeys`。
- `LFENV-ROOT-0002` 代表 Envir 继续通过严格物理候选构建。`Market_Prices/*.prc`、`Market_Upg/*.upg` 和市场保存目录保持运行/二进制领域数据边界，不以文本猜测解析；全版本依赖结论留 LFENV-15/18。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0
```

结果：生成成功，0 警告，0 错误。

专项命令：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --filter "FullyQualifiedName~LingFengCommerceContentProviderTests|FullyQualifiedName~PhysicalTextFileProviderTests|FullyQualifiedName~TxtScriptReloadCoordinatorTests|FullyQualifiedName~Recipe|FullyQualifiedName~NameList|FullyQualifiedName~GameShopStateTests|FullyQualifiedName~KillSwitch|FullyQualifiedName~RepresentativeEnvir" -p:WarningLevel=0 --logger "trx;LogFileName=lfenv13-targeted.trx" --results-directory Docs/Evidence/LFENV-13-20260816
```

专项结果：78/78 通过，0 失败，0 跳过。TRX：`lfenv13-targeted.trx`。

随后执行 Base05 全量回归：867/867 通过，0 失败，0 跳过，用时 2 分 15 秒。TRX：`lfenv13-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录、外部语料根和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。目录内 `.gitattributes` 固定 TRX 为 CRLF，保证干净检出后的哈希稳定。

说明书新增页和导航已完成文件存在性、相对链接和 YAML 静态检查。当前 Python 环境未安装 `mkdocs`，`python -m mkdocs build --strict` 返回 `No module named mkdocs`，因此未把站点渲染伪报为通过；该环境缺失不影响服务端代码和 Base05 门禁。

## 双轴审查结论

- Spec 自审：无 BLOCKER。阶段退出项均有真实可观察链：商城权限/余额/库存/邮件、配方背包事务、名单查询、C#/TXT 优先级、领域热更摘要与失败回滚。服务型商品、货币类型 4 和二进制市场数据均明确保留事实或延后，不冒充完整兼容。
- Standards 自审：无 BLOCKER。商业与名单 Schema 已拆为独立 Provider；候选事实只读，发布和玩家事务沿用主线程；数据库保存基线未污染；测试加入既有禁并行集合并对称恢复 Settings、Provider、全局物品/邮件 ID、商品和脚本状态。

## SHA-256

- `lfenv13-targeted.trx`：`5D989F5EDB03464073253C81F6D841FEAB7CF8FA66758607E0194FDEA72DDFDC`
- `lfenv13-full.trx`：`4D36BDBC2D96CBA1109739144183D6BB8CCEEA54D26A0C14D5A17D15C9C543A7`

哈希对应双轴自审和测试隔离修复后的最终实现与测试工作树；后续阶段若修改商业 Provider、规则名单、热更摘要、商城购买或合成事务，必须重跑并刷新证据。
