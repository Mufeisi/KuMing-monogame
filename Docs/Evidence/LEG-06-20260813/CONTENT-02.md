# CONTENT-02 地图叠层与跨域导航证据

- 状态：已完成
- 执行日期：2026-08-13
- 事实源：[`../../requirements/LEG-06-内容生产工作台.md`](../../requirements/LEG-06-内容生产工作台.md)、代码与测试输出
- 分支：`codex/leg-06-map-overlays-navigation`

## 可运行工件

`VisualMapInfo` 顶部“叠层”菜单提供出口、NPC、刷怪、矿区四个独立开关。出口以蓝色点标记、NPC 以金色点标记；刷怪和矿区继续复用既有区域高亮，因此作者可在同一画布比较四类内容，同时仍在右侧既有页签编辑刷怪或矿区。

“诊断定位”直接消费 `ProjectSemanticPreflight.ValidateMapContent` 的真实 LEG-02 地图/NPC报告，并以稳定 `Source` 解析地图、出口、NPC 和刷怪拥有者。刷怪/矿区在当前工作台选中真实控件；出口安全关闭工作台后定位 `MapInfoForm` 的真实出入点；NPC 安全关闭后进入 `NPCInfoForm` 并选择稳定实体索引。关闭前仍执行 CONTENT-01 的保存、放弃、继续编辑门禁。

## 自动化验证

```text
MapContentNavigation/EditingSession/ProjectSemanticPreflight 聚焦测试：13 通过，0 失败
Server.ContentAuthoringIntegration.Windows：4 通过，0 失败
Base05.Tests 全量：416 通过，0 失败
LyoCrystal.Windows.slnf Release：0 错误（45 个历史警告）
git diff --check：通过
```

Windows STA 宿主验证：四层可同时可见；逐一关闭任一层时其他三层保持可见；LEG-02 刷怪诊断选中真实 `RespawnEntry`；出口定位真实 `MovementInfoListBox` 记录；NPC 使用稳定实体索引定位既有编辑器记录。

本地 TRX：

- `Tests/Base05.Tests/TestResults/leg06-content02-base05.trx`
- `eng/WindowsIntegration/Server.ContentAuthoringIntegration/TestResults/leg06-content02-windows.trx`

TRX 按仓库规则忽略，不进入提交。

## 审查与边界

双轴审查发现的重复诊断规则、动态菜单/GDI 资源释放、索引语义、模态窗体释放和测试穿透内部实现问题均在提交前返工。当前实现不修改地图格式、数据库 Schema、协议或地图/NPC事实对象；草稿只用于只读预检投影。

回滚本切片提交即可移除叠层与导航入口；本切片本身不产生数据迁移或自动内容写入。
