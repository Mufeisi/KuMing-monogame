# GUI-05 校验、签名与资源限制证据

- 执行日期：2026-08-14
- 切片：`GUI-05`
- 分支：`codex/leg-07-gui-validation-signing`
- 语言：中文

## 交付工件

1. `CustomGuiValidationPolicy` 统一校验 Schema 语义、对象图、共享布局结果、文本、资源和数量限制，输出稳定 `GUI05-*` 诊断。
2. `CustomGuiResourceCatalog` 将逻辑资源、字体和图集绑定到既有 Bootstrap 资源清单，不保存第二份物理资源事实。
3. `CustomGuiPackageVerifier` 直接读取签名 ZIP 的固定入口，复用 `BootstrapManifestSignaturePolicy` 与 `BootstrapSignedPackageHashPolicy`，并保护文档修订降级和同修订内容替换。
4. 明确限制文档、资源绑定、控件、深度、列表、文本、输入、包体、ZIP 条目及解压总量；路径穿越、重复条目、篡改与超限均失败关闭。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CustomGuiValidationTests` | 预期编译失败：校验、资源目录和签名包门卫尚不存在 |
| GUI-05 定向领域测试 | 同上 | 5/5 通过 |
| PC 客户端 Release 构建 | `dotnet build src/Clients/Client_VorticeDX11/Client_VorticeDX11.csproj -c Release --no-restore` | 0 错误；既有警告不由本切片扩张处理 |
| Android Shared Release 构建 | `dotnet build src/Clients/Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -c Release -f net10.0-android --no-restore` | 0 错误；既有警告不由本切片扩张处理 |
| 作者工具 Release 构建 | `dotnet build src/Launcher/Launcher.Editor/Launcher.Editor.csproj -c Release --no-restore` | 0 错误、0 警告 |
| Base05 Release 全量 | `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore` | 448/448 通过 |
| Windows Release 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj -c Release --no-restore` | 115/115 通过 |

## 可观察行为

1. 合法文档、逻辑资源、字体和图集经 Bootstrap 资源清单解析后无诊断；缺失物理资产和重复逻辑标识被拒绝。
2. 父级循环、非法容器、负尺寸、父级/安全区越界、对象数和嵌套超限分别返回稳定诊断，不创建运行控件。
3. 文本/富文本、字体、输入、列表单项与总量、全文本总量均有边界及失败样例。
4. 真实临时签名 ZIP 可通过既有信任链；篡改、降级、同修订换内容、包体超限、ZIP 路径穿越、重复条目和描述超限全部拒绝。

## 接缝与回滚

校验层不下载、不渲染、不修改运行描述，也不创建新的证书或清单格式。回滚本切片独立提交即可删除 `GUI-05` 门卫；`GUI-01..04` 的 Schema、作者工具与双端 Adapter 不受影响。尚未进入 `GUI-06` 的静态签名包不属于已接受发布事实。
