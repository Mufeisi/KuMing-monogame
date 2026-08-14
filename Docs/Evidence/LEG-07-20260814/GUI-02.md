# GUI-02 作者工具游戏 GUI Adapter 证据

- 执行日期：2026-08-14
- 切片：`GUI-02`
- 分支：`codex/leg-07-gui-authoring-adapter`
- 语言：中文

## 交付工件

1. `CustomGuiCanvasDocument` 把 `CustomGuiRuntimeDocument` 映射为设计核心公共接口；运行描述继续是唯一运行事实，锁定状态以独立作者元数据保存。
2. 新建项目包含一份 1280×720、9 个对象的活动窗口文档，覆盖 Schema v1 全部首版控件类型。
3. “设计”顶级模式内部增加“启动器界面/游戏界面”文档页；游戏 GUI 沿用 190px 对象树、中央画布、250px 属性栏，不增加顶级导航或永久侧栏。
4. 对象树、画布点击、方向键移动、属性编辑、显隐、锁定、对齐、撤销/重做和保存重载均通过设计核心或 Adapter 接缝修改文档。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore --filter "FullyQualifiedName~GameGuiAuthoringUsesDesignCoreAndPersistsAuthorMetadataSeparately\|FullyQualifiedName~DesignModeSwitchesBetweenLauncherAndGameGuiWithoutAddingWorkspaceSidebar"` | 预期编译失败：项目文档、Adapter 与窗口证据接口尚不存在 |
| GUI-02 定向测试 | 同上 | 2/2 通过 |
| 设计核心 Release 全量 | `dotnet test Tests/Launcher.DesignCore.Tests/Launcher.DesignCore.Tests.csproj --no-restore -c Release` | 11/11 通过 |
| 启动器 Windows Release 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore -c Release` | 107/107 通过 |
| 后台窗口冒烟 | `dotnet run --project src/Launcher/Launcher.Editor/Launcher.Editor.csproj --no-build -c Release -- --editor-ui-smoke artifacts/leg07-gui02-20260814-final` | 退出码 0；未移动鼠标、未抢占焦点 |

## 窗口工件

1. `artifacts/leg07-gui02-20260814-final/游戏GUI画布设计器.png`：1280×800 完整作者工作区；对象树、中央活动窗口画布、属性栏和双文档页均可见，无重叠或重复侧栏。
2. `artifacts/leg07-gui02-20260814-final/最小窗口画布设计器.png`：1100×700 最小窗口；三栏仍完整可用，属性值和保存状态可见。

## 回滚

本切片没有协议、数据库、客户端运行 Adapter 或玩家状态变更。回滚独立提交即可删除编辑器项目中的游戏 GUI 文档/作者元数据、Adapter 和工作区页；`GUI-01` Schema 与示例保持不变。
