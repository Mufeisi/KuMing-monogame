# P1-MOUNT 坐骑使用探针任务

- 任务：ANDROID-04 坐骑请求、权威状态与 UI 投影冒烟。
- 状态：待审核；使用探针冒烟已通过。
- 分支：`codex/p1-mount-probe-20260809`。
- 工作树：本地隔离工作树（路径已脱敏）。
- 基线：`20a137c`。
- 文件所有权：`Tests/Base05.Tests/MobileMountStateTests.cs` 与本证据目录。
- 做：复用真实 `Chat(@ride)` 请求、`MountUpdate` 响应与 `MobileMountState` 公开 UI 投影。
- 不做：不改生产代码、协议、服务端、FairyGUI、PRD，不构造缺失坐骑资源。
- 方法约束：必须复用现有状态/UI 接缝；禁止新增通用探针框架；同一问题两次无进展立即停止。
- 预估时间：不超过 30 分钟；超过 2 倍预算停止报告。
- 完成定义：使用探针与坐骑专项通过，证据归档，独立只读复核无阻塞。
- 语言：中文；代码标识符、命令和原始报错除外。

## 验收结果

- `MobileMountStateTests`：8/8 通过，失败 0，跳过 0。
- 专用探针：`Mount_usage_probe_smoke_projects_ride_request_and_server_update_to_ui_values`。
- 覆盖：`Chat(@ride)` 请求 → `MountUpdate` 权威响应 → FairyGUI 使用的坐骑类型、乘骑状态与按钮可用性投影。
- 窗口证据：复用已合并的坐骑专用 fallback 与不误命中排名组件证据；缺失真实坐骑资源不再阻塞本任务。
