# GUI-CORE-04 内存 Adapter 与核心门禁证据

- 执行日期：2026-08-14
- 切片：`GUI-CORE-04`
- 分支：`codex/leg-07-core-gate`
- 语言：中文

## 交付工件

1. 设计核心领域测试统一以 `ICanvasDocument<TId>` 作为被测接口；Fixture Adapter 只负责构造、故障注入和变更回调，所有结果断言均来自文档公共可观察行为。
2. 四档 DPI 验证增加文字容纳门禁，并用真实画布选择、移动、撤销和重做后的启动器快照执行保存前预检。
3. 后台证据窗口在收到真实 `WM_DPICHANGED` 后恢复目标监视器应有客户区，避免屏幕外窗体被宿主工作区限幅；生产 `DpiChanged` 处理未改变。
4. 点击区域验证按顶层同级控件 Z 序命中，既支持透明图像按钮，也能发现被其他可见控件遮挡的点击目标。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | 画布编辑后执行四档 DPI 预检 | 预期失败：200% 时控件按 2 倍布局而证据窗体受工作区限幅；修复证据接缝后转绿 |
| DPI 定向回归 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release --filter "FullyQualifiedName~CanvasEditedWidescreenProjectPassesFourDpiPreflightWithoutClipping|FullyQualifiedName~PerMonitorV2WindowProcessesRealDpiMessageAndHitTests"` | 13/13 通过 |
| 核心领域测试 | `dotnet test Tests/Launcher.DesignCore.Tests/Launcher.DesignCore.Tests.csproj --no-restore -c Release` | 11/11 通过 |
| 启动器 Windows 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release` | 105/105 通过 |
| Launcher 解决方案构建 | `dotnet build LyoCrystal.Launcher.slnf --no-restore -c Release` | 0 错误；2 个既存 WindowsBase 版本冲突警告 |
| 正式自包含发布 | `dotnet publish src/Launcher/Launcher.Editor/Launcher.Editor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true ...` | 通过 |
| 中文 UI 后台冒烟 | 发布后的编辑器执行 `--editor-ui-smoke`，窗口隐藏 | 退出码 0 |
| 完整发行后台冒烟 | 发布后的编辑器执行 `--editor-smoke`，窗口隐藏 | 退出码 0 |

仓库全解决方案的无还原构建另行探测，因 Android、iOS 和若干工具项目缺少既存 `project.assets.json` 而未形成有效全仓门禁；本切片使用包含全部 Launcher 项目的解决方案筛选器、受影响测试和正式自包含发布完成验证。

## 真实工件与截图

- 工件根：`artifacts/leg07-gui-core-gate-20260814-accepted/`
- 工件数量：362 个文件；总大小 193,304,351 字节。
- 玩家入口：`full-smoke/smoke-client/smoke-project-玩家入口.exe`，9,651,986 字节，SHA-256 `9D1BCDB18614EE49F8E24F9601A001CFAE73929163C807692E5AC3B7AB5BE3A9`。
- 运行截图：100% 为 1196×719、125% 为 1495×899、150% 为 1794×1078、200% 为 2392×1438；均通过越界、文字和点击区域门禁。
- 作者工作区：保存 1280×800 可视化画布、1100×700 最小窗口、中文傻瓜配置、高级设置和发行体概览截图；人工复核未见重复侧栏、遮挡或裁切。
- 完整冒烟还生成两次签名发布、回滚发布、离线发布包和密钥恢复包，退出码为 0。

## 回滚

回滚本切片独立提交即可恢复 `GUI-CORE-03`；不包含游戏 GUI Schema、PC/Android 游戏 Adapter、Shared 协议或服务端状态变更。后台证据尺寸修正与新增 DPI 断言必须一起回滚，避免留下与证据语义不匹配的测试。
