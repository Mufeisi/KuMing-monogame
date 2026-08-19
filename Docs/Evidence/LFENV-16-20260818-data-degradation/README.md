# LFENV-16 数据缺项降级为运行时安全报告

- 状态：已实施（数据缺项侧关闭）
- 负责人：项目所有者
- 最后复核日期：2026-08-18
- 事实源：源代码（Envir / Drop Provider / Dependency Manifest）、Base05 专项与全量回归
- 阶段主记录：[`../LFENV-16-20260817/README.md`](../LFENV-16-20260817/README.md)（本文件保留数据降级决策与当时诊断，最终阶段结论以主记录为准）

## 目标

将"数据库缺失"（E1 的物品/怪物/地图静态引用缺项）从**启动硬阻断**降级为**运行时安全的数据报告**：如实结构化报告，但不阻止服务启动。仅保留真正会阻塞运行的依赖（E2 客户端契约、领域 Adapter 缺失）为启动级失败关闭。

## 判定依据（数据缺项为运行时安全的 no-op/判否）

实测酷明真实 Envir + 源库 + 源地图后确认，901 项 E1 数据缺项全部落在运行时安全路径：

| 缺口类别 | 数量(引用) | 运行时行为 |
|---|---|---|
| ItemName（物品名） | 783 | `GIVEITEM/TAKEITEM` no-op 记日志；`CHECKITEM` 判否；商店 Goods `info==null` 跳过 |
| ItemIndex（物品编号） | 9 | 同上检查类 |
| Monster（怪物） | 95 | `MONGEN/GIVEPET` 目标不存在 → 不生成/不发 |
| Map（地图） | 14 | 实测为 `IsonMap/CheckMap` 检查命令（非 MOVE/传送），玩家不在图即判否，无空图传送 |

全部不崩溃、不破坏主线程、不产生越界或空图传送。最早的地图缺口样本（`Szpt97/98/99`、`Fhys`、`R01`、`Y10`、`Zm`、`Twdt`）经核查全部来自 `IsonMap` 检查，非传送。

## 变更清单

1. `Docs/design/scripting/翎风服务器常量与整服Envir直接运行实施规格.md`：E1 定义（§3.2 外部依赖契约）、LFENV-15/16 状态表、LFENV-16 收口说明、§10 失败策略，统一为"数据缺项如实报告、不阻断启动；E2 客户端契约/领域 Adapter 缺失失败关闭"。
2. `src/Server/Server/Scripting/LingFengExternalDependencyManifest.cs`：新增 `BlocksStartup(kind)` 单一事实源（仅 `ClientContract`/`DomainAdapter` 为启动硬阻断）。
3. `src/Server/Server/MirEnvir/Envir.cs`：`ValidatePhysicalExternalDependenciesWhenReady` 将 `report.Missing` 分为 hardBlocking（E2 契约/Adapter，失败关闭）与 dataDegraded（数据缺项，`MessageQueue` 降级日志，不阻断）。
4. `src/Server/Server/Scripting/LingFengMonsterDropProvider.cs`：`ValidateDependencies` 对"找不到物品"（`IsMissingItemError`）降级记录，不入启动阻断集合；结构坏行/循环/括号仍失败关闭。
5. `src/Server/Server/Scripting/CSharpDropTableProvider.cs`：`IsMissingItemError` 由 private → internal，供 Drop 校验复用。
6. 测试：
   - `LingFengCompleteSliceTests.cs`（酷明审计）：断言升级为"缺口全为数据缺项且如实报告不误报"。
   - `LingFengExternalDependencyManifestTests.cs`：`缺失E1数据依赖降级不阻断且报告缺失`（数据缺项不再 throw、缺失仍进 Missing）。
   - `LingFengMonsterDropProviderTests.cs`：`DependencyValidation_缺物品降级不报而结构坏行仍阻断`。

## 验证命令与结果

```powershell
dotnet build LyoCrystal.Windows.slnf --configuration Release --no-restore
```

结果：`0` 错误，`517` 条既有编译警告（与历史基线一致）。

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~LingFengMultiVersionMatrixTests"
```

结果：`1060` 通过 / `5` 失败 / `0` 跳过。5 个失败均与本次改动无关（既有）：

| 失败用例 | 归属 |
|---|---|
| `封神原版五行灵珠…` | LFENV-16 既有结构性红测（见 LFENV-16-20260817 记录） |
| `LingFengEnvirCorpusCatalogTests.…漂移` | 本机缺 `rg`（`process.Start` 失败）环境问题 |
| `翎风物品参数…` | 变量比较 `EQUAL` 断言，与依赖/掉落改动无交集 |
| `ServiceInstanceRuntimeTests` ×2 | 真实服务组件环境（"组件 server 退出，代码 1"） |

以上结果是 2026-08-18 降级改动刚完成时的阶段性诊断，不是本轮收口后的最终基线。2026-08-19 完成相关回归修正并将封神原版完整 E2 显式标记为外部资源阻塞后，重新执行未过滤的 Base05 全量：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --configuration Release --no-build --logger "trx;LogFileName=lfenv16-closeout-20260819.trx" --logger "console;verbosity=minimal"
```

最终结果：`1064` 通过 / `0` 失败 / `1` 跳过，共 `1065` 项；唯一跳过项为封神原版五行灵珠完整 E2。完整阶段结论以 [`../LFENV-16-20260817/README.md`](../LFENV-16-20260817/README.md) 为准。

专项（本次改动相关，全部通过）：
`LingFengExternalDependencyManifestTests`（6/6）、`LingFengCompleteSliceTests` 酷明审计（1/1）、`PhysicalTextFileProviderTests`/`CommerceContentProviderTests`/`EnvirFileClassifierTests`（合计 67/67）。

## 语义边界

- 审计/报告层 `LingFengDependencyReport.Success` 语义不变（仍 `Missing.Count==0`）：数据缺项仍使 `Success=false`，供诚实审计与诊断。
- 生产启动层：数据缺项不再 throw（降级日志），仅 E2 硬阻断 throw。二者分离，避免"把不完整数据冒充已满足"。
