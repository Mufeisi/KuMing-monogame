# CONTENT-03 NPC 与脚本闭环证据

- 日期：2026-08-13
- 状态：已实施
- 事实源：[`../../requirements/LEG-06-内容生产工作台.md`](../../requirements/LEG-06-内容生产工作台.md)、代码与测试输出
- 回滚：逐项回滚 CONTENT-03 提交；NPC 数据保存仍由既有 `Envir.SaveDB` 入口负责

## 用户可见结果

`NPCInfoForm` 顶部新增“保存、重载、差异”显式会话入口。窗体字段只修改 NPC 草稿；取消、关闭时放弃以及保存失败均不改变原始 NPC 对象。保存前复用 `ProjectSemanticPreflight` 的 LEG-02 NPC 诊断，错误会阻断提交。

“脚本闭环”页按真实 NPC 脚本名显示脚本来源、对话页面、页面正文、下一跳链接和稳定诊断。悬空页面链接、缺少 `[@MAIN]` 与空脚本可在打开脚本前发现；C# 脚本通过现有 `ScriptDebugForm` 定位，TXT 脚本继续使用既有打开方式，NPC 图片可定位到现有预览资源目录。

## 自动化证据

- `NpcContentAuthoringTests`：6 个领域测试，覆盖草稿隔离、显式提交、索引高水位、失败恢复、对话页投影和链接诊断。
- `Server.ContentAuthoringIntegration.Windows`：7 个 STA 窗口测试，覆盖 NPC 会话入口、草稿重载、保存成功、LEG-02 阻断、保存失败恢复、脚本闭环入口和脚本调试器指定文件定位，并回归 CONTENT-01/02 窗口行为。
- Base05 全量：421/421 通过；TRX 为 `Tests/Base05.Tests/TestResults/leg06-content03-base05.trx`。
- Windows 集成：7/7 通过；TRX 为 `eng/WindowsIntegration/Server.ContentAuthoringIntegration/TestResults/leg06-content03-windows.trx`。
- `LyoCrystal.Windows.slnf` Release：构建 0 错误、45 个既有警告。
- 双轴审查：规格与工程规范阻断项均归零；`git diff --check` 通过。
