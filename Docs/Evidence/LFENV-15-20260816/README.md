# LFENV-15 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：物品、怪物、地图、客户端契约、未接管领域配置的候选依赖清单与发布前失败关闭

## 工件摘要

- `PhysicalTextFileProvider` 把四个既有领域 Provider 的已解析模型和统一 TXT Tokenizer 归并成不可变 `ExternalDependencyManifest`，没有另建平行语法解释器。
- E1 抽取静态物品名/编号、怪物名和物理地图/逻辑别名；E2 追加动态脚本引用、`MonIcons/NpcIcons`、未接管领域文件及 `Market_Prices/Market_Upg` 二进制领域契约。
- 候选输出 `Accepted/RuntimeData/ExternalDependency/Rejected` 四类计数。LFENV-ROOT-0002 真实目录重算证明四类总和等于文件总数、`Rejected=0`，且五类实际依赖均非空。
- `TxtScriptsDependencyLevel=None/E1/E2` 明确声明门禁等级。缺失依赖在变量目录、脚本和领域 Provider 被替换前阻断；测试先验证缺物品拒绝并保留基线，再补齐同一物品并成功切换候选。
- E2 客户端/领域确认清单默认空并失败关闭，不创建假物品、假怪物、假地图或假客户端资源。诊断只含逻辑键和相对来源，最多展开 200 条，避免超大日志。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项覆盖依赖清单、物理 Provider、文件所有权、爆率、怪物内容、世界、商店/配方和严格快照：91/91 通过，0 失败，0 跳过。TRX：`lfenv15-targeted.trx`。

随后执行 Base05 全量回归：879/879 通过，0 失败，0 跳过，用时 2 分 33 秒。TRX：`lfenv15-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录、工作区和本机语料绝对路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数不变。目录内 `.gitattributes` 固定 TRX 为 CRLF。

执行 `python -m mkdocs build --strict --config-file Manual/Engine/mkdocs.yml` 返回 `No module named mkdocs`；当前环境未安装 `mkdocs`，未把缺失工具伪报为站点构建通过。

## 双轴自审结论

- Spec 自审：无 BLOCKER。五类依赖进入同一候选，E1/E2 分层、四类文件计数、启动前阻断、满足后发布和上一快照保留均有可观察断言；本阶段没有冒充 LFENV-16 的完整玩法 E2。
- Standards 自审：无 BLOCKER。默认 `None` 保持既有配置行为；验证发生在主线程发布边界之前；结果和清单只读；探针按依赖键缓存；明细有硬上限；日志无宿主绝对路径或个人信息。

## 每日工件检查

- 工件：生产代码、测试、操作说明、规格状态、可执行 TRX 共五类；过程资产仅本证据摘要和双轴自审，工件数量高于过程资产。
- 语言：代码标识符与命令保留英文，其余文档、状态和提交信息均使用中文。
- 边界：未触碰用户已有 `Docs/index.md` 修改和 TXT-12 Android 截图；未下载、复制或伪造外部数据库/客户端资源。

## SHA-256

- `lfenv15-targeted.trx`：`88BB97C13068F05018D8DEE9D93109C5CF4C4C580CE152E3628BE9C836E5FF05`
- `lfenv15-full.trx`：`752E0C70DB89C2D67C53B8CA844F81418AE9766E74EF6BFC9EC65E7A35E3EC47`

哈希对应最终 LFENV-15 工作树；后续若修改依赖抽取、文件分类、Provider 模型或发布门禁，必须重跑并刷新证据。
