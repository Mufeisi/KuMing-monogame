# GUI-01 游戏 GUI 运行描述 Schema 证据

- 执行日期：2026-08-14
- 切片：`GUI-01`
- 分支：`codex/leg-07-gui-schema`
- 语言：中文

## 交付工件

1. `Shared.CustomGui` 提供 Schema v1、运行文档、九个具体控件类型、视口、安全区、锚点、边距、横纵流布局和静态列表项模型。
2. `CustomGuiDocumentCodec` 使用源生成 JSON 元数据和唯一序列化入口，输出 UTF-8 camelCase 字段及字符串枚举。
3. 解析时字段名区分大小写，并拒绝未知控件、未知属性、未知枚举、整数枚举、缺失必填字段和不兼容 Schema 版本；稳定诊断为 `GUI01-SCHEMA-001/002`。
4. 中文规范固定线格式、布局算法、对象图、兼容与失败关闭语义；示例运行描述覆盖全部首版控件能力。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~CustomGuiSchemaTests"` | 预期编译失败：`Shared.CustomGui` 与运行文档尚不存在 |
| Schema 定向测试 | 同上 | 8/8 通过 |
| Base05 全量 | `dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore -c Release` | 440/440 通过 |
| 启动器 Windows 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release` | 105/105 通过 |
| Android 共享客户端构建 | `dotnet build src/Clients/Client_MonoGame.Shared/Client_MonoGame.Shared.csproj --no-restore -c Release -f net10.0-android` | 0 错误、0 警告 |
| 示例读取 | `仓库示例运行描述由生产Codec直接读取` | 文档标识正确，9 个元素完整读取 |

## 示例工件

- 路径：`Docs/samples/custom-gui/new-player-event.v1.json`
- 大小：4,067 字节。
- SHA-256：`5E46026063A3B8CDFCA212ED0818ABF322D43935BC40D00539CAA396115B7E9B`。
- 内容：Window、Panel、Image、RichText、List、ItemSlot、ProgressBar、TextInput 与 Button；逻辑资源只使用 `assetId`，不包含本地路径、URL 或脚本。

## 回滚

本切片没有数据库、协议、渲染 Adapter 或玩家状态变更。回滚独立提交即可删除 v1 Schema、Codec、测试、规范和示例；进入后续签名资源兼容窗口后，必须按运行描述规范保留 v1 Reader 或先回退已接受资源版本。
