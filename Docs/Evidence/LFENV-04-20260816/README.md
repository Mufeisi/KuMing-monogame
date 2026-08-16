# LFENV-04 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：统一服务器常量文本渲染、嵌套扫描、结构化诊断与资源限额

## 工件摘要

- `ScriptTextRenderer`：只识别服务端 `<$...>`，统一调用 `IServerSymbolResolver`；普通中文、按钮和客户端 `$$...` 原样保留。
- 闭合扫描使用显式栈并在解析阶段执行嵌套上限，避免深嵌套输入先触发运行时栈耗尽。
- 语法或限额失败整行原子回退；单项解析失败保留原占位符并返回脱敏诊断。
- 本阶段不接管 `NPCSegment.ReplaceValue`；真实 P0 Adapter 与领域入口切换属于 LFENV-05。

## 自动化验证

专项命令：

```powershell
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LingFengScriptTextRendererTests|FullyQualifiedName~LingFengServerSymbolResolverTests" --logger "trx;LogFileName=lfenv-04-targeted.trx"
```

专项结果：26/26 通过，0 失败，0 跳过。TRX：`lfenv-04-targeted.trx`。

随后执行 Base05 全量回归：729/729 通过，0 失败，0 跳过，用时 2 分 19 秒。TRX：`lfenv-04-full.trx`。提交版 TRX 已脱敏用户名、设备名和用户目录，不改变测试计数与结果。

## SHA-256

- `lfenv-04-targeted.trx`：`B19346180F8BF4B5D8410235D79CDA3EFD2AD1F98EDC90EA6AB2818DC7C04C80`
- `lfenv-04-full.trx`：`F5880EFBB4CB4FB1468437AC20049271DC57A29F4209FD3E6CD865E451020B0E`

哈希对应双轴审查修复后的最终实现与测试工作树；后续阶段若修改本模块或专项测试，必须重跑并刷新证据。
