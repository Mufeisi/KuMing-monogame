# GUI-CORE-02 深模块提取证据

- 执行日期：2026-08-14
- 切片：`GUI-CORE-02`
- 分支：`codex/leg-07-design-core-deepening`
- 语言：中文

## 交付工件

1. `CanvasDocument` 统一承载布局变更、对齐、分布、可见、锁定、删除/恢复、层级顺序、撤销/重做和变更通知。
2. `LauncherCanvasDocument` 删除对应算法，只保留启动器主题映射和启动器专有样式变更。
3. 核心提供 `DESIGN-GEOMETRY-001/002` 稳定诊断，分别表示对象越出画布和非正尺寸。
4. 完整快照历史默认最多保留 100 步；测试用 3 步容量证明旧历史会被淘汰且不会继续撤销。

## 测试与门禁

| 门禁 | 命令 | 结果 |
|---|---|---|
| TDD 红灯 | `dotnet test Tests/Launcher.DesignCore.Tests/Launcher.DesignCore.Tests.csproj --no-restore --configuration Release` | 预期失败：通用编辑接口尚不存在 |
| 核心领域测试 | 同上 | 11/11 通过 |
| 启动器画布专项 | `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj --no-restore --configuration Release --filter "FullyQualifiedName~Canvas"` | 9/9 通过 |
| 启动器 Windows 全量 | 去除筛选运行同一项目 | 104/104 通过 |
| Launcher 解决方案构建 | `dotnet build LyoCrystal.Launcher.slnf --no-restore --configuration Release` | 0 错误；2 个既存 WindowsBase 版本冲突警告 |
| 差异格式 | `git diff --check` | 通过，仅 Git 换行提示 |

## 性能与容量

内存 Adapter 建立 500 个设计对象并选中全部对象，先预热 5 次，再连续执行 60 个交替方向的真实拖动采样；选择耗时必须低于 50ms，拖动 P95 必须低于 50ms，调度尖峰最大值必须低于 100ms。核心与内存 Adapter 均以标识索引消除重复线性扫描。历史样例分别覆盖保存点仍在容量窗口、保存点已被淘汰及撤销后分支截断，逐步断言 `IsDirty` 在当前态、已保存态和最早保留态之间正确变化。

## 回滚

回滚本切片的独立提交即可恢复 `GUI-CORE-01` 接缝；`LauncherCanvasDocument` 和 `ICanvasDocumentAdapter` 必须同时回滚，禁止留下接口版本不匹配。
