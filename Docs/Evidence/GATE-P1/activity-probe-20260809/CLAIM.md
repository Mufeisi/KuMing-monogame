# P1-ACTIVITY 活动赏金使用探针任务

- 任务：ANDROID-07 活动请求、服务端状态与 UI 投影冒烟。
- 状态：待审核；使用探针冒烟已通过。
- 分支：`codex/p1-activity-probe-20260809`。
- 工作树：本地隔离工作树（路径已脱敏）。
- 基线：`1f07b0f`。
- 文件所有权：`Tests/Base05.Tests/MobileActivityStateTests.cs` 与本证据目录。
- 做：复用真实 `AcceptQuest`、`ChangeQuest` 与 `MobileActivityState` 公开 UI 投影。
- 不做：不改生产逻辑、协议、服务端、FairyGUI、活动规则、奖励、脚本或 PRD。
- 方法约束：必须复用现有状态/UI 接缝；禁止新增通用探针框架；同一问题两次无进展立即停止。
- 预估时间：不超过 40 分钟；超过 2 倍预算停止报告。
- 完成定义：使用探针与活动专项通过，证据归档，独立只读复核无阻塞。
- 语言：中文；代码标识符、命令和原始报错除外。

## 验收结果

- `MobileActivityStateTests`：16/16 通过，失败 0，跳过 0。
- 专用探针：`Activity_usage_probe_smoke_projects_accept_request_and_server_change_to_ui_values`。
- 覆盖：`AcceptQuest` 请求 → 请求门控 → `ChangeQuest(Add)` 权威响应 → 门控清除、选中活动与“每日活动”UI 投影。
- 窗口证据：复用已合并的活动专用 fallback、缓存切换和分页证据；真实活动开放不再阻塞本任务。
