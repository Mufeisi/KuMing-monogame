# P1-FISHING 钓鱼使用探针任务

- 任务：ANDROID-06 抛竿请求、服务端状态与 UI 投影冒烟。
- 状态：待审核；使用探针冒烟已通过。
- 分支：`codex/p1-fishing-probe-20260809`。
- 工作树：本地隔离工作树（路径已脱敏）。
- 基线：`8884fc4`。
- 文件所有权：`Tests/Base05.Tests/MobileFishingStateTests.cs` 与本证据目录。
- 做：复用真实 `FishingCast`、`FishingUpdate` 与 `MobileFishingState` 公开 UI 投影。
- 不做：不改生产逻辑、协议、服务端、FairyGUI、掉落表、地图或 PRD。
- 方法约束：必须复用现有状态/UI 接缝；禁止新增通用探针框架；同一问题两次无进展立即停止。
- 预估时间：不超过 30 分钟；超过 2 倍预算停止报告。
- 完成定义：使用探针与钓鱼专项通过，证据归档，独立只读复核无阻塞。
- 语言：中文；代码标识符、命令和原始报错除外。

## 验收结果

- `MobileFishingStateTests`：12/12 通过，失败 0，跳过 0。
- 专用探针：`Fishing_usage_probe_smoke_projects_cast_request_and_server_update_to_ui_values`。
- 覆盖：`FishingCast` 抛竿请求 → 请求门控 → `FishingUpdate` 权威响应 → 鱼竿/卷轴、进度、概率、坐标与命中 UI 投影。
- 设备入口：沿用已合并的逍遥主界面“钓鱼”入口证据；真实地图钓点不再阻塞本任务。
