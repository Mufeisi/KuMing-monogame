# 翎风 TXT 兼容审计清单

- 状态：已实施
- 负责人：项目所有者
- 最后复核日期：2026-08-16
- 事实源：翎风说明书快照、当前源码、指定真实脚本快照和自动化测试

本目录保存 `TXT-00` 与 `LFENV-01/02` 的可筛选审计工件，不作为运行时配置输入：

- `lingfeng-txt-topics.csv`：CHM 的 1,012 个目录主题；用于保留分类和来源路径。
- `lingfeng-txt-compatibility.csv`：从命令、检测和触发正文中抽取并逐条人工复核的去重兼容清单。
- `lingfeng-txt-corpus-usage.csv`：指定真实脚本快照的原始 DSL、结构化 C#、迁移注释和占位符用法基线。
- `lingfeng-envir-roots.csv`：`D:\ChuanQi\服务端` 下 53 个 Envir 根的版本家族、角色、文件统计和内容哈希画像。
- `lingfeng-envir-file-ownership.csv`：LFENV-09 Envir 文件唯一归属规则、脚本发布权限和未匹配阻断策略。
- `lingfeng-server-symbols.csv`：用户附件与真实 Envir 语料合并后的服务器只读常量目录；服务器常量与 `#DEFINE` 自定义常量严格分开。

## 当前快照

- 翎风说明书根：`D:\ChuanQi\工具端\引擎\PC端\LFM2_chm_extracted`
- 说明书快照版本：`LFM2-2026-08-15-snapshot`
- 全文件清单摘要：`73FD0FBD3EA7208FC76F6201EA615059DDC52BB324422BCCDF426EFAA44323E8`
- 目录主题：1,012 个，其中动作 342、检测 136、触发 62。
- 当前兼容条目：496 个；其中首轮命令/触发候选 453 个，TXT-06 把基础变量资料页拆为 13 个独立变量条目，TXT-07 至 TXT-09 补入 22 个真实语料高频命令条目，TXT-10 桥接人物登录与升级触发，TXT-11 桥接战斗前后、拾取完成、怪物死亡与掉落完成事件并补入 3 个伤害前置条目，TXT-12 补入 3 个既有对话与协议事实项，TXT-13 补入 1 个受控 URL 安全替代，并复核 `CHECKMAPNAME`。当前分布为 B 51、C 10、D 327、E 83、X 25、`?` 0。
- 真实脚本根：`D:\ChuanQi\Crystal_monogame\Server-mono\Envir`
- 真实内容文件：2,348 个 `.cs`；其中 121 个构造 `TextFileDefinition`。

主题数不是命令数。一个主题可能包含多个命令，也可能只是教程、配置说明或失效链接；兼容率只能按完成去重和正文复核后的命令/触发条目计算。

## 状态门禁

状态只允许 `A/B/C/D/E/X/?`。任何 A、B、C 条目必须同时填写实现位置、测试编号、说明书页面和最后复核日期。验证命令：

```powershell
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --filter FullyQualifiedName~LingFengTxtCompatibilityCatalogTests --no-restore
```

该命令验证主题分类基线、唯一键、状态集合和已支持条目的证据链。它不代替逐页正文复核、原引擎差分或真实运行测试。

## LFENV-01/02 快照

- 画像日期：2026-08-16。
- Envir 根：53 个，合计 68,140 个文件、580,922,342 字节，归入 24 个版本家族；文本编码画像为 UTF-8 27,399、UTF-8 BOM 922、CP936 候选 38,940、含 NUL 异常文本 2。
- 服务器常量目录：905 行；其中 281 个附件原始表达式逐条保留，限定 53 个 `Envir*` 根后有 513 个归一化符号族实际出现、共 627,292 次；不计部署目录、`.history` 和 Hidden/System/ReparsePoint 文件。
- 当前目录阶段只允许 D（已登记、待实现验证）和 X（安全拒绝）；“旧替换分支存在”不会冒充完整兼容。
- 定向验证：`LingFengEnvirCorpusCatalogTests` 5/5 通过（含本机真实语料重算）；Base05 全量回归 703/703 通过；证据见 `Docs/Evidence/LFENV-01-02-20260816/`。

验证命令：

```powershell
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~LingFengEnvirCorpusCatalogTests
```

## LFENV-09 文件所有权

- 分类顺序固定为路径安全、备份归档、运行数据、可执行工件、文档附件、客户端契约、脚本、领域配置和未归属阻断。
- 只有 `LFENV09-SCRIPT` 可进入 `PhysicalTextFileProvider`；运行数据、客户端资源、备份、文档和领域配置均不会被脚本热更新覆盖。
- `QFunction-0.txt` 同时存在于根目录和 `Market_Def` 时，按翎风标准目录优先使用 `Market_Def`；只有根级文件时作为兼容回退，禁止发布为普通 NPC 页。
- 代表 Envir 的每个非隐藏、非系统、非重解析点文件必须唯一归属；任何 `LFENV09-INVALID-*` 或 `LFENV09-UNASSIGNED` 都拒绝候选快照。
