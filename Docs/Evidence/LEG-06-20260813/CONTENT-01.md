# CONTENT-01 地图与刷怪编辑会话证据

- 状态：已完成
- 执行日期：2026-08-13
- 事实源：[`../../requirements/LEG-06-内容生产工作台.md`](../../requirements/LEG-06-内容生产工作台.md)、代码与测试输出
- 分支：`codex/leg-06-map-authoring-session`

## 可运行工件

既有 `VisualMapInfo` 已接入显式内容编辑会话。窗口顶部提供“撤销”“重做”“校验与差异”“保存”“取消”；`Ctrl+Z`、`Ctrl+Y`、`Ctrl+S` 可执行同类操作。关闭窗口时若存在未保存差异，会要求保存、放弃或继续编辑，不再无提示地把控件内容写回 `MapInfo`。

编辑会话保持原始 `MapInfo` 不变，直到作者确认保存。保存前使用 LEG-02 稳定刷怪诊断码和新增矿区诊断检查怪物引用、数量、范围、刷新时间及地图边界，并生成逐项新增、修改、删除差异。持久化失败时原地恢复原列表内容并保留列表对象身份，避免其他编辑界面持有陈旧引用。

## 自动化验证

```text
MapContentEditingSessionTests：4 通过，0 失败
Base05.Tests 全量：412 通过，0 失败
Server.ContentAuthoringIntegration.Windows：1 通过，0 失败
LyoCrystal.Windows.slnf Release：0 错误
git diff --check：通过
```

本地 TRX：

- `Tests/Base05.Tests/TestResults/leg06-content01-base05.trx`
- `eng/WindowsIntegration/Server.ContentAuthoringIntegration/TestResults/leg06-content01-windows.trx`

TRX 属于按仓库规则忽略的过程工件。真实 STA WinForms 宿主验证了五个可见入口和默认不提交状态。当前会话缺少可调用的桌面截图运行时，因此本切片未生成视觉截图；`CONTENT-02` 接入叠层时再执行窗口视觉走查。

## 边界与回滚

本切片未修改数据库 Schema、协议、地图文件格式或运行时刷怪逻辑。回滚本切片提交即可恢复旧界面；已由新界面成功保存的内容属于数据变更，须使用保存前源文件备份恢复，不能仅依赖代码回滚。
