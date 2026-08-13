# PROTO-03 协议与资源兼容矩阵

## 1. 当前兼容基线

| 消费者 | 当前程序版本 | 正式协议源 | wire 基线 | 客户端资源兼容版本 | 当前随包资源版本 | 服务端准入 |
|---|---|---|---|---:|---|---|
| PC `Client_VorticeDX11` | Assembly `1.0.0.0` / `Globals.ProductVersion=Release` | `src/Shared/Shared/Shared.csproj` | `wire-v1` | `1.0.0` | `content-988b1bb85432df58363d3b307b7971157680b207fcd3213f12eb520c032176c9` | `CheckVersion=true` 时，客户端可执行文件哈希必须在服务端 `VersionPath` 白名单中 |
| Android `Client_MonoGame.Android` | Display `2.0.0`、Application `20000`、Assembly `1.0.0.0` | `Client_MonoGame.Shared` 直接链接 `Shared` 协议 C# 文件 | `wire-v1` | `2.0.0` | `content-988b1bb85432df58363d3b307b7971157680b207fcd3213f12eb520c032176c9` | 与 PC 使用同一 `ClientVersion` wire 包；正式服仍执行可执行文件哈希白名单 |
| Server `Server.Library` | Assembly `1.0.0.0` / `Globals.ProductVersion=Release` | `src/Shared/Shared/Shared.csproj` | `wire-v1` | 不适用 | 不适用 | 只接受本表所列 `wire-v1` 包布局及已登记客户端构建哈希 |

`wire-v1` 的当前范围为客户端到服务端 ID `0..144`、服务端到客户端 ID `0..274`。权威语义清单是 `Docs/generated/protocol/protocol-wire-manifest.json`；自动生成的结构、字段、枚举、序列化 IL 与源文件摘要在 `Docs/generated/protocol/protocol-wire-manifest.generated.json`。

## 2. 最低兼容规则

1. 协议最低版本为 `wire-v1`。仓库当前没有 `wire-v2`；新增或改变包 ID、字段顺序、字段类型、枚举数值、读写逻辑时，生成清单必然漂移，必须显式审查兼容影响，不能静默覆盖。
2. PC 的资源兼容版本固定从 `PcBootstrapLayout.ClientCompatibilityVersion = 1.0.0` 传入；Android 固定从 `ClientResourceLayout.BootstrapClientCompatibilityVersion = 2.0.0` 传入。
3. SEC-06 签名资源清单的 `MinimumClientVersion` 高于当前端兼容版本时，该端失败关闭；签名清单内每个包的 SHA-256 仍不可关闭地校验。
4. `ResourceVersion` 是签名资源集合标识，不等同于 wire 版本。资源更新可以在 `wire-v1` 内独立推进，但不得绕过最低客户端版本、签名、包授权和防降级状态。
5. 服务端的 `CheckVersion` 是构建哈希白名单，不是 SemVer 比较。发布新 PC 或 Android 构建时，RELEASE 流程必须先登记对应构建哈希；未登记构建不视为兼容。

## 3. 允许与拒绝矩阵

| 服务端 | 客户端 | 资源清单 | 结果 |
|---|---|---|---|
| 当前 `wire-v1` | 当前 PC `wire-v1` / 资源兼容 `1.0.0` | 最低版本 `<=1.0.0` 且签名、哈希、防降级均通过 | 允许；仍须通过服务端构建哈希白名单 |
| 当前 `wire-v1` | 当前 Android `wire-v1` / 资源兼容 `2.0.0` | 最低版本 `<=2.0.0` 且签名、哈希、防降级均通过 | 允许；仍须通过服务端构建哈希白名单 |
| 当前 `wire-v1` | 任一端 | 清单最低版本高于该端资源兼容版本 | 拒绝资源更新并保留上一可运行资源 |
| 当前 `wire-v1` | wire manifest 与服务端不一致 | 任意 | 构建/CI 漂移门禁失败；不得发布 |
| 当前 `wire-v1` | 构建哈希未登记 | 任意 | `CheckVersion=true` 时拒绝登录 |

## 4. 维护方式

- 修改 `Shared` 协议事实源后，运行：
  `dotnet run --project Tools/ProtocolManifestGenerator/ProtocolManifestGenerator.csproj -c Release -- --write Docs/generated/protocol/protocol-wire-manifest.generated.json`
- 提交前及 CI 运行 `--verify`。清单不一致返回非零退出码。
- `src/Clients/Client_MonoGame.Shared/Share` 中的历史协议副本不再进入 Android 正式构建，只由 `Tests/ShareProtocolCompat` 保留为 PROTO-01 旧 wire 差异夹具；本任务不修改该副本。
- 本矩阵记录当前已实现兼容边界；密钥注入、发布流水线和设备验收分别属于 RELEASE-01～03。
