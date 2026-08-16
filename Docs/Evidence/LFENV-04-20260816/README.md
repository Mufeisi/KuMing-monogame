# LFENV-04 验证证据

- 状态：已实施
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

专项结果：16/16 通过，0 失败，0 跳过。TRX：`lfenv-04-targeted.trx`。

随后执行 Base05 全量回归：719/719 通过，0 失败，0 跳过，用时 2 分 15 秒。TRX：`lfenv-04-full.trx`。提交版 TRX 已脱敏用户名、设备名和用户目录，不改变测试计数与结果。

## SHA-256

- `lfenv-04-targeted.trx`：`87ECF79B2EAB70C98970B94C5BD55CE68D59C0447D0667CB29E39E14464BC2E8`
- `lfenv-04-full.trx`：`5ABFA9BAD5DC7886AB3941EAAC2BD5B68D2F23C2B6A9AB2FA762DED2AD57D3BF`

哈希对应双轴审查前的完整实现与测试工作树；审查若要求修改源码或测试，必须重跑并刷新证据。
