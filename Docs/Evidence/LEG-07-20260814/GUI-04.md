# GUI-04 Android/FairyGUI 运行 Adapter 证据

- 执行日期：2026-08-14
- 切片：`GUI-04`
- 分支：`codex/leg-07-gui-android-adapter`
- 语言：中文

## 交付工件

1. `MobileCustomGuiAdapter` 消费共享 Schema 与布局引擎，输出与平台工厂解耦的移动节点树；同一运行描述在 720×1280 与 1024×768 视口具有确定缩放和留边结果。
2. `FairyGuiCustomGuiFactory` 将九类节点映射为内嵌 FairyGUI 控件，支持窗口/面板裁剪、包资源图片、普通/富文本、按钮、输入、列表、进度和物品格。
3. `IFairyGuiCustomAssetResolver` 只把逻辑 `assetId` 解析为现有 `UIPackage` URL；Adapter 不读任意文件、不建立资源下载器或第二渲染器。
4. Host 通过既有 FairyGUI 容器挂载，释放根节点即可释放整棵树；工厂异常也会回收已创建节点。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release --filter "FullyQualifiedName~MobileCustomGuiAdapterTests"` | 预期编译失败：移动 Adapter 接口尚不存在 |
| PC/移动 Adapter 联合定向 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-build --no-restore -c Release --filter "FullyQualifiedName~MobileCustomGuiAdapterTests|FullyQualifiedName~PcCustomGuiAdapterTests"` | 8/8 通过 |
| Android Shared Release 构建 | `dotnet build src/Clients/Client_MonoGame.Shared/Client_MonoGame.Shared.csproj --no-restore -c Release -f net10.0-android` | 0 错误；既有警告不由本切片扩张处理 |
| Base05 Release 全量 | `dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-restore -c Release` | 443/443 通过 |
| Windows Release 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release` | 115/115 通过 |

## 可观察行为

1. 1280×720 参考文档投影到 720×1280 时缩放为 0.5625，左右无留边、上下各 437.5px 留边；窗口和子控件保持父级相对坐标。
2. 九个 Schema v1 元素均经工厂获得独立节点类型，列表静态项、按钮动作标识和资源逻辑标识保持不变；序列化前后文档字节一致。
3. 父级循环在工厂创建前以共享 `GUI03-LAYOUT-001` 失败关闭；工厂中途抛出时已创建根与子树全部释放。
4. Android 真实工厂已由 `net10.0-android` Release 编译证明与当前内嵌 FairyGUI API 接通；真实双端画面留到 `GUI-06` 统一验收，避免在中间切片伪造截图。

## 渲染接缝与回滚

移动节点只进入现有 `FairyGuiHost -> Stage/FairyBatch` 绘制路径；`CMain` 仍按原顺序结束主场景 `SpriteBatchStack`、绘制 FairyGUI、再进入后续层，没有改写 FairyGUI、`SpriteBatchStack` 或 MonoGame 渲染器。回滚本切片独立提交即可删除移动 Adapter 与项目链接，`GUI-01..03` 的 Schema、作者工具和 PC Adapter 不受影响。
