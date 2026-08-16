# LFENV-03 验证证据

- 状态：已实施
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：服务器常量 Resolver、Catalog、Context、Reference、Value 与结构化结果类型

## 工件摘要

- `src/Server/Server/Scripting/ServerSymbols/`：只读常量解析深模块；不修改人物、物品、地图或其他领域对象。
- `LingFengServerSymbolResolverTests.cs`：只跨 `IServerSymbolResolver` seam 验证规范名、别名、参数、区域无关格式、失败状态、安全拒绝、异常隔离和上下文隔离。
- 权威规格已记录 LFENV-03 的实施边界；本阶段不接管旧 `NPCSegment.ReplaceValue`，不把 LFENV-02 目录中的 D 项升级为已兼容。

## 自动化验证

专项命令：

```powershell
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~LingFengServerSymbolResolverTests --logger "trx;LogFileName=lfenv-03-targeted.trx"
```

专项结果：7/7 通过，0 失败，0 跳过。TRX：`lfenv-03-targeted.trx`。

随后执行 Base05 全量回归：710/710 通过，0 失败，0 跳过，用时 2 分 25 秒。TRX：`lfenv-03-full.trx`。提交版 TRX 已脱敏用户名、设备名和用户目录，不改变测试计数与结果。

## SHA-256

- `lfenv-03-targeted.trx`：`018483E2FB69E370FB097AA7538B7891BEE88E640BB3068A0735A26DD8BA2A7F`
- `lfenv-03-full.trx`：`583757AE4CCA8EBE2BAF166BA76C93D6E605858C0166E64C7EA0D44A48F572E6`

哈希对应本阶段最终实现与测试工作树。后续阶段若修改本模块或专项测试，必须重跑并刷新证据。
