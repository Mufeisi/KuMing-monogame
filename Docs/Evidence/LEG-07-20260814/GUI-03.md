# GUI-03 PC 运行 Adapter 证据

- 执行日期：2026-08-14
- 切片：`GUI-03`
- 分支：`codex/leg-07-gui-pc-adapter`
- 语言：中文

## 交付工件

1. `CustomGuiLayoutEngine` 是作者工具、PC 和后续 Android 共用的布局事实源，覆盖父级相对坐标、中心/边缘锚点、拉伸、边距、内边距、间距和横纵流布局。
2. `PcCustomGuiAdapter` 将 Schema v1 九类对象映射为现有 MirControls 控件树，并提供等比 `fit` 缩放、居中留边、场景附着和统一释放。
3. 面板和静态控件由 `MirControl` 既有纹理路径绘制；文字复用 `MirLabel`；图片资源通过 `assetId -> MLibrary` 接缝并最终调用 `MLibrary.Draw`，没有第二套 PC 渲染器。
4. Adapter 在布局父级无效或循环时先失败关闭，不遗留半成品控件树；稳定诊断码为 `GUI03-LAYOUT-001`。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release --filter "FullyQualifiedName~PcCustomGuiAdapterTests"` | 预期编译失败：PC Adapter、Host 与控件映射尚不存在 |
| PC Adapter 定向测试 | 同上 | 3/3 通过 |
| Shared Schema/布局定向测试 | `dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~CustomGuiSchemaTests"` | 10/10 通过 |
| GUI-02/03 联合回归 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release --filter "FullyQualifiedName~GameGuiAuthoringUsesDesignCoreAndPersistsAuthorMetadataSeparately\|FullyQualifiedName~DesignModeSwitchesBetweenLauncherAndGameGuiWithoutAddingWorkspaceSidebar\|FullyQualifiedName~PcCustomGuiAdapterTests"` | 5/5 通过 |
| Base05 Release 全量 | `dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore -c Release` | 443/443 通过 |
| Windows Release 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release` | 110/110 通过 |

## 可观察行为

1. 1280×720 参考文档在 1920×1080 目标视口以 1.5 倍无留边物化；在 1024×768 以 0.8 倍物化并产生顶部/底部各 96px 留边。
2. 全部 9 个运行元素均形成 MirControl，标题、静态列表项、进度比例、按钮白名单动作标识及资源解析状态可由 Host 接口观察。
3. Host 可附着到既有场景根控件，释放时由 MirControl 所有权链移除并释放子控件。

## 回滚

本切片没有协议、服务端状态、资源清单或 Android 变更。回滚独立提交即可删除共享布局引擎与 PC Adapter，并把作者 Adapter 恢复到上一提交内的局部布局解析；`GUI-01/02` 的 Schema 和作者文档仍保持可读。
