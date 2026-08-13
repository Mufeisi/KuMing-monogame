# LEG-04 启动器作者体验完成证据

- 日期：2026-08-13
- 分支：`codex/leg-04-launcher-authoring`
- 范围：可视化画布、编辑历史、四档 DPI 预检和真实玩家入口
- 语言：中文，代码标识符、命令和原始测试字段除外

## 工件

1. `LauncherCanvasDocument.cs`：画布状态、选择、移动缩放、吸附、对齐分布、层级、锁定隐藏及撤销重做。
2. `LauncherCanvasEditorPanel.cs`：控件树、运行时预览画布、属性面板和四态资源入口。
3. `LauncherRuntimeHost.CaptureControlLayoutForEditor`：从真实运行时提取编辑器初始布局。
4. `Program --editor-smoke`：四档 DPI、画布截图、真实玩家入口、签名双版本、回滚、离线包和恢复包纵向冒烟。
5. `LauncherEditorTests`：九项画布回归、客户区坐标和玩家快照兼容性保护。

## 验证结果

```text
Launcher.Editor Release build: 通过，0 警告，0 错误
画布局部回归: 通过 9，失败 0
Launcher.PlayerShellIntegration.Windows 布局修正后最终全量: 通过 95，失败 0
Base05.Tests 全量: 通过 408，失败 0
当前源码编辑器纵向冒烟: 退出码 0
真实玩家入口: Client 进程响应正常，主窗口标题为“管理员离线配置器验收”
```

完整测试结果：

- `eng/WindowsIntegration/Launcher.PlayerShellIntegration/TestResults/leg04-full-final.trx`
- `eng/WindowsIntegration/Launcher.PlayerShellIntegration/TestResults/leg04-layoutfix-final.trx`
- `Tests/Base05.Tests/TestResults/leg04-full.trx`

## 运行工件

最终本地工件目录：`artifacts/leg04-editor-full-20260813-current-1`（构建产物不提交）。包含：

- `editor-ui.png`：可视化画布、控件树和多选预览；
- `editor-preview-100.png`、`125.png`、`150.png`、`200.png`：四档 DPI 预检；
- `smoke-client/smoke-project-玩家入口.exe`：当前源码模板生成的真实玩家入口；
- `signed-publish/`：首发、第二版及回滚后的签名发布历史；
- `smoke-project-离线发布.zip` 与密钥恢复包。

用户目检反馈后的布局修正将项目选择收进顶部下拉框，左侧只保留控件树；画布改为客户区渲染以消除窗口边框偏移，工具栏压缩为菜单，属性区支持折叠并在多选时显示共同属性。最终布局截图位于本地 `artifacts/leg04-editor-ui-layoutfix-20260813/可视化画布设计器.png`。

## 兼容性与回滚

- 玩家快照没有新增编辑器专属字段；专门测试断言发布 JSON 不包含 `Locked` 或 `ZIndex`。
- 锁定状态只随编辑器项目保存，层级继续使用既有控件列表顺序。
- 回滚代码提交即可移除画布能力，无数据库、协议或资源迁移。
