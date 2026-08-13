# CONTENT-04 掉落分析闭环证据

- 日期：2026-08-13
- 状态：已实施
- 事实源：[`../../requirements/LEG-06-内容生产工作台.md`](../../requirements/LEG-06-内容生产工作台.md)、代码与测试输出
- 回滚：逐项回滚 CONTENT-04 提交；TXT 保存使用同目录临时文件替换，代码回滚不替代内容备份

## 用户可见结果

现有 `DropBuilder` 新增“概率分析与变更审查”面板，提供分析、差异、显式保存和重载。编辑、分类添加、金币修改和文本接受只更新会话草稿，不再隐式覆盖 TXT；校验失败、保存失败、切换取消和关闭取消均保留原始内容。

TXT 草稿使用稳定 `CONTENT04-DROP-001` 格式诊断并复用 LEG-02 掉落校验；保存通过同目录临时文件原子替换。C# 掉落定义直接读取现有 `DropTableDefinition`，支持打开时分析快照与当前期望产出对比，不建立第二事实源。

概率展开显示可证明的结构概率和期望值；固定种子模拟通过 `CSharpDropTableProvider` 构建生产 `DropInfo`，再调用与运行时共享的 `AttemptDropWithRandom` 算法。含 `Condition` 且缺少上下文时明确显示“不可计算”并跳过数值模拟。

宽屏显示右侧分析面板，1100×700 时自动折叠并提供恢复按钮；新增功能文字为 12pt，支持 `Ctrl+S` 保存与 `F6` 焦点循环。完整后台截图：[`1280×800`](CONTENT-04-drop-workspace-1280x800.png)、[`1100×700`](CONTENT-04-drop-workspace-1100x700.png)、[`分析面板`](CONTENT-04-drop-analysis-panel.png)。生成过程使用屏幕外、禁止激活窗体，不移动鼠标、不模拟键盘、不占用桌面焦点。

## 自动化证据

- `DropContentAuthoringTests`：11/11 通过，覆盖草稿隔离、原子文件提交、TXT 解析/LEG-02、理论期望、真实运行时模拟、Random/First/金币、条件与嵌套条件、C# 快照差异。
- `Server.ContentAuthoringIntegration.Windows`：13/13 通过，其中 CONTENT-04 为 6 个窗口用例，覆盖显式保存/重载/失败恢复、C# 与 TXT 边界、1100/1280 布局、四档 DPI 与后台截图；并回归 CONTENT-01～03 窗口行为。
- Base05 全量：433/433 通过；TRX 为 `tests/Base05.Tests/TestResults/leg06-content04-base05.trx`。
- Windows 集成全量：13/13 通过；TRX 为 `eng/WindowsIntegration/Server.ContentAuthoringIntegration/TestResults/leg06-content04-windows.trx`。
- `Server.Library` Release：构建 0 错误、8 个既有警告。
- `Server.MirForms` Release：构建 0 错误、472 个既有警告。
- 双轴审查：模拟重复实现、条件数值、C# 对比、GUI 字号/DPI/截图阻断均已修复；最终复审阻断项归零。
- `git diff --check`：通过。
