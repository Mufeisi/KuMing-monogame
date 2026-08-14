# GUI-CORE-01 设计核心基线与接缝审计证据

- 执行日期：2026-08-14
- 切片：`GUI-CORE-01`
- 分支：`codex/leg-07-design-core-baseline`
- 语言：中文

## 交付工件

1. 新增无 UI 项目 `src/Launcher/Launcher.DesignCore`，公共接口只包含元素标识、矩形、可见/锁定状态、快照及变更操作。
2. 选择、移动、缩放、吸附、撤销/重做、脏状态和统一历史已迁入 `CanvasDocument<TId, TSnapshot>`。
3. `LauncherCanvasDocument` 通过 `LauncherCanvasAdapter` 映射既有 `LauncherTheme` 和编辑状态；旧选择集合、吸附算法和历史实现已删除。
4. 启动器的布局、样式、可见性、锁定和层级命令继续进入同一核心历史，未建立第二套撤销栈或主题文档。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test Tests/Launcher.DesignCore.Tests/Launcher.DesignCore.Tests.csproj` | 预期失败：核心接口尚不存在 |
| 核心领域测试 | `dotnet test Tests/Launcher.DesignCore.Tests/Launcher.DesignCore.Tests.csproj --no-restore --configuration Release` | 6/6 通过 |
| 启动器画布专项 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore --filter "FullyQualifiedName~Canvas"` | 9/9 通过 |
| 启动器 Windows 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore --configuration Release` | 104/104 通过 |
| 核心 Release 构建 | `dotnet build src/Launcher/Launcher.DesignCore/Launcher.DesignCore.csproj --no-restore --configuration Release` | 0 警告、0 错误 |
| Launcher 解决方案构建 | `dotnet build LyoCrystal.Launcher.slnf --no-restore --configuration Release` | 0 错误；2 个既存 WindowsBase 版本冲突警告 |
| 差异格式 | `git diff --check` | 通过，仅 Git 换行提示 |

## 可观察行为

- 内存 Adapter 证明选择后移动可吸附到同级元素边缘，并可撤销、重做。
- 锁定或隐藏元素不参与编辑，越界移动被限制在画布内。
- Adapter 发起的非几何变更与核心几何命令共享同一撤销顺序。
- 核心程序集依赖检查拒绝 WinForms、FairyGUI、MonoGame 和 Vortice 引用。

## 回滚

回滚本切片的独立提交，同时恢复 `LauncherCanvasDocument` 与项目引用；不得只删除核心项目而保留 Adapter 调用。
