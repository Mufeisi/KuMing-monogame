# GATE-P1：PROTO-01 / BASE-10 / PERF-00 证据

## 结论

截至 2026-08-09，本工作树在基线提交 `6229cdc35eea70bd3b56224130a2f07a1470c4c7` 上完成了本任务要求的复核：

- PROTO-01：`ProtocolGoldenTests` 通过 11/11；`Docs/protocol-wire-manifest.json` 可由 .NET `System.Text.Json.JsonDocument` 解析。
- BASE-10：`ResourceManifestTests` 通过 9/9；`ResourceBaseline.ps1 Validate Repository` 通过，仓库范围资源与 `None` 资源契约符合清单。
- PERF-00：`PerformanceMetricsTests` 通过 14/14；验证的是指标接缝、会话生命周期和 JSON 导出，不是真实压力基线。
- Base05 全量：通过 198/198，失败 0，跳过 0。

本证据只覆盖自动化与清单/契约复核，不宣称 Android 真机闭环，也不宣称 S1/S2/S3（含 300 连接/100 活跃角色）真实压力基线已完成。真实压力基线仍受 PERF-00 文档列出的服务器、账号、地图和受控连接负载入口限制。

## 基线与环境

| 项目 | 值 |
|---|---|
| 任务分支 | `codex/p1-evidence-20260809` |
| 基线提交 | `6229cdc35eea70bd3b56224130a2f07a1470c4c7`（`收口SEC-02 C4.3连接安装与回调重试`） |
| 操作系统 | Windows 10 专业工作站版，版本 `10.0.19045` |
| .NET SDK | `10.0.200` |
| .NET 运行时 | `10.0.4` |
| PowerShell | `pwsh 7.4.7` |
| 协议清单来源提交 | `3e96959`（清单/兼容审计） |
| 资源清单来源提交 | `1bcc13c`（版本/哈希契约） |
| PERF-00 相关来源提交 | `3e303ac`、`4a90a60`、`c3649ba` |

清单文件 SHA-256（用于本次复核定位）：

```text
Docs/protocol-wire-manifest.json  3121CBEA1CB8A84E94CFEE45B41A44EDEF9F7AC763500AAEB2ABE80E4EAB5FC8
resources.manifest.json           48EAE88CE21C759ABEDCF2B8E05E293C127960BC12008C5B235F0C1FD7EF53B3
```

## 逐条命令与结果

所有命令均在仓库根目录执行。首次使用 `--no-restore` 时因该独立工作树尚未生成 `obj/project.assets.json` 退出 1；随后按正常流程还原一次，后续命令均使用 `--no-restore`。

初次探测命令：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~ProtocolGoldenTests" --logger "console;verbosity=normal"
退出码：1（NETSDK1004：缺少 project.assets.json，尚未进入测试阶段）
```

该退出码是独立工作树的依赖还原前置问题，不计入专项测试失败；还原后同一命令已按下文结果重新执行。

### 0. 还原依赖

```text
dotnet restore Tests/Base05.Tests/Base05.Tests.csproj --nologo
退出码：0
```

还原过程有现存 NuGet 安全公告：`log4net 3.0.3`（NU1902）和 `SQLitePCLRaw.lib.e_sqlite3 2.1.11`（NU1903）；本任务不改依赖。

### 1. PROTO-01 专项

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~ProtocolGoldenTests" --logger "console;verbosity=normal"
退出码：0
测试总数：11
通过数：11
失败数：0
跳过数：0
耗时：2.4437 秒
```

清单机器可读性复核使用 .NET JSON 文档解析器（退出码 0）：

```powershell
$json=[IO.File]::ReadAllText((Resolve-Path Docs/protocol-wire-manifest.json));
$doc=[System.Text.Json.JsonDocument]::Parse($json);
$root=$doc.RootElement;
$enums=$root.GetProperty('enums');
$packets=$root.GetProperty('packets');
$enumCount=@($enums.EnumerateObject()).Count;
[pscustomobject]@{
  schemaVersion=$root.GetProperty('schemaVersion').GetString();
  sourceCommit=$root.GetProperty('sourceCommit').GetString();
  packets=$packets.GetArrayLength();
  enums=$enumCount;
  bytes=([Text.Encoding]::UTF8.GetByteCount($json));
} | ConvertTo-Json -Compress
$doc.Dispose()
```

输出：

```json
{"schemaVersion":"PROTO-01.wire-manifest.v1","sourceCommit":"0f2a933","packets":420,"enums":61,"bytes":475371}
```

说明：Windows PowerShell 5.1 的 `ConvertFrom-Json` 对清单中仅大小写不同的枚举键（例如 `DemonWolf`/`Demonwolf`）不兼容并退出 1；该入口不是本次采用的机器读取器。`System.Text.Json.JsonDocument` 可正常读取，且专项测试中的清单覆盖/枚举声明校验通过。未修改清单或测试以规避该环境差异。

### 2. BASE-10 资源专项

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~ResourceManifestTests" --logger "console;verbosity=normal"
退出码：0
测试总数：9
通过数：9
失败数：0
跳过数：0
耗时：36.5516 秒
```

清单根契约读取（退出码 0）：

```powershell
$m=Get-Content -Raw -Encoding UTF8 resources.manifest.json | ConvertFrom-Json;
[pscustomobject]@{
  manifestVersion=$m.manifestVersion;
  resources=@($m.resources).Count;
  contractAcquire=$m.contract.acquire.action+'/'+$m.contract.acquire.scope;
  contractValidate=$m.contract.validate.action+'/'+$m.contract.validate.scope;
  source=$m.resources[0].source.type
} | ConvertTo-Json -Compress
```

输出：

```json
{"manifestVersion":"2026-08-07","resources":7,"contractAcquire":"Acquire/All","contractValidate":"Validate/Repository|All","source":"repository"}
```

仓库范围校验使用 `pwsh`（PowerShell 7，退出码 0）：

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/ResourceBaseline.ps1 -Action Validate -Scope Repository
```

关键输出：

```text
[OK] mobile-ui-retro 目标：32 files, 77437699 bytes, sha256=e795d309cc33f7b70971d689850ceaab6474aef9b24fa5ca1ffe0ed201825fda
[OK] mobile-ui-micro-variant 目标：31 files, 77932526 bytes, sha256=7438d5138b65b3c227236e3fda4a73c2f2e4036713bb5ad41f6f618386e419ca
[OK] mobile-content 目标：2 files, 8227740 bytes, sha256=877020e652b9a42b7a21f2af24dd1d36816205fbb1d42168f3c18e067ba89ea3
[SKIP] mobile-bootstrap-assets：外部资源未在 Repository 范围验证。
[SKIP] pc-runtime-assets：外部资源未在 Repository 范围验证。
[SKIP] patch-repository：外部资源未在 Repository 范围验证。
[OK] test-resources absence 目标：按 absence 契约目标不存在。
资源基线通过。
```

`Repository` 输出的 3 条 `[SKIP]` 不是 3 个外部资源：其中 2 个是 `local-authorized` 外部资源（`mobile-bootstrap-assets`、`pc-runtime-assets`），1 个是 `generated`/`Export` 资源（`patch-repository`）。`Tools/ResourceBaseline.ps1` 的 `Get-ExternalResources` 明确排除 `generated`，所以 `patch-repository` 不属于 `ResourceBaseline.ps1 Acquire`；它必须由 `Tools/Mobile-BootstrapPackageRepoExport.ps1` 先导出。后续在两项外部资源按 `Acquire -Scope All` 准备好、补丁仓库完成 `Export` 后，再运行 `ResourceBaseline.ps1 -Action Validate -Scope All`，才能验证全部最终资源。

### 3. PERF-00 专项

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~PerformanceMetricsTests" --logger "console;verbosity=normal"
退出码：0
测试总数：14
通过数：14
失败数：0
跳过数：0
耗时：1.4314 秒
```

这些测试覆盖禁用不累积、会话启停、冻结导出、同路径并发导出、队列高水位跨会话重基线、GC/暂停分离、百分位子桶和不可用指标原因保留。样本由测试在临时目录生成并清理，不产生真实网络连接，也不包含 S1/S2/S3 压测动作；因此不能作为性能基线数字。

### 4. Base05 全量

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore --nologo --logger "console;verbosity=normal"
退出码：0
测试总数：198
通过数：198
失败数：0
跳过数：0
耗时：1.2548 分钟
```

全量输出中出现若干 `ServerLifecycleSmokeTests` 临时目录删除失败的 `IOException` 提示，但对应测试均报告通过，测试运行最终为 198/198；该现象作为环境清理提示保留，不在本任务范围内修复。

## 限制与门禁解释

1. 本证据只证明清单可读、资源契约/仓库范围校验可执行、性能采集代码自动验证通过。
2. PERF-00 的 S1（1 连接/0 活跃）、S2（100/100）和 S3（300/100）尚未真实运行。当前仓库没有受控连接负载入口、测试账号、地图和服务器资源，严禁用单元测试、模拟连接或合成 JSON 代替。
3. `ResourceBaseline.ps1 Validate Repository` 只校验仓库范围；当前跳过分类为 2 个 `local-authorized` 外部资源 + 1 个 `generated`/`Export` 补丁仓库，不能据此宣称 `Validate All` 或发布资源已齐备。`patch-repository` 不走 `Acquire`，必须先执行导出器，再执行 `Validate All`。
4. 本任务不修改生产代码、测试代码、PRD、README、架构报告或现有清单；只新增本目录证据。

## 本任务结论

P1-EVIDENCE 的自动化退出条件已满足：PROTO-01、BASE-10、PERF-00 专项及 Base05 全量均为绿，清单和资源验证结果可复核。GATE-P1 仍需等待 P1-VERIFY-A/B 的 Android 真机功能证据与集成会话复核；本 README 不单独关闭 GATE-P1。
