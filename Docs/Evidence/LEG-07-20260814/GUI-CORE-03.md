# GUI-CORE-03 启动器 Adapter 接回证据

- 执行日期：2026-08-14
- 切片：`GUI-CORE-03`
- 分支：`codex/leg-07-launcher-adapter-cutover`
- 语言：中文

## 交付工件

1. 无 UI 核心新增 `ICanvasDocument<TId>`，统一对象枚举、选择、布局、状态、层级、历史、诊断和 Adapter 扩展变更入口。
2. `LauncherCanvasEditorPanel`、`LauncherObjectTreeAdapter` 与 `LauncherPropertyInspectorAdapter` 仅持有核心接口，不再读取或修改具体 `LauncherCanvasDocument.Controls`。
3. Launcher 专有外观通过内部只读投影和单对象样式 Adapter 映射；属性检查器通过核心 `ChangeEditableSelection` 完成批量修改，锁定过滤、快照、撤销和失败回滚仍由核心负责。
4. 生产 `LauncherCanvasAdapter` 对控件和编辑状态建立标识索引，历史恢复先完整构造替换列表与索引再同步重建，避免重复线性查找和陈旧索引。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test Tests/Launcher.DesignCore.Tests/Launcher.DesignCore.Tests.csproj --no-restore` | 预期失败：`ICanvasDocument<TId>` 尚不存在；随后预期失败：接口尚无 `ChangeEditableSelection` |
| 核心领域测试 | `dotnet test Tests/Launcher.DesignCore.Tests/Launcher.DesignCore.Tests.csproj --no-restore --configuration Release` | 11/11 通过 |
| 启动器画布专项 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore --filter "FullyQualifiedName~Canvas"` | 9/9 通过 |
| 启动器 Windows 全量 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore --configuration Release` | 104/104 通过 |
| Launcher 解决方案构建 | `dotnet build LyoCrystal.Launcher.slnf --no-restore --configuration Release` | 0 错误；2 个既存 WindowsBase 版本冲突警告 |
| UI 依赖扫描 | `rg -n "LauncherCanvasDocument|LauncherCanvasAlignment|LauncherCanvasDistribution|LauncherCanvasLayoutChange|ChangeSelectionStyle"`（三个 UI 文件） | 无命中 |
| 差异格式 | `git diff --check` | 通过，仅 Git 换行提示 |

Windows 全量继续覆盖四档 DPI、三栏工作区、对象树选择与显隐、属性编辑、画布移动/吸附/撤销重做和既有截图工件；本切片未改变布局尺寸或新增可见 UI。

## 回滚

回滚本切片独立提交即可恢复 `GUI-CORE-02` 的具体 Launcher 文档依赖；核心接口、Launcher Adapter 和三个 UI 消费者必须一起回滚，禁止留下不匹配接口或绕过核心历史的样式修改路径。
