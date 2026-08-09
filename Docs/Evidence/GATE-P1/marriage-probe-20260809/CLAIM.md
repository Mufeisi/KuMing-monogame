# P1-MARRIAGE 关系使用探针任务

- 任务：ANDROID-03 关系请求、响应、状态与 UI 投影冒烟。
- 状态：待审核；使用探针冒烟已通过。
- 分支：`codex/p1-marriage-probe-20260809`。
- 工作树：本地隔离工作树（路径已脱敏）。
- 基线：`8df8d61`。
- 文件所有权：`Tests/Base05.Tests/MobileMarriageStateTests.cs` 与本证据目录。
- 做：复用真实 `MarriageRequest`、`MarriageReply`、`LoverUpdate` 协议类型和 `MobileMarriageState` 公开 UI 投影。
- 不做：不改生产协议、服务端、FairyGUI、PRD，不构造双账号设备环境。
- 方法约束：必须复用现有状态/UI 接缝；禁止新增通用探针框架或修改生产逻辑；同一问题两次无进展立即停止。
- 预估时间：不超过 30 分钟；超过 2 倍预算停止报告。
- 完成定义：使用探针与关系专项通过，证据归档，独立只读复核无阻塞。
- 语言：中文；代码标识符、命令和原始报错除外。

## 验收结果

- `MobileMarriageStateTests`：14/14 通过，失败 0，跳过 0。
- 专用探针：`Marriage_usage_probe_smoke_projects_request_response_and_server_state_to_ui_values`。
- 覆盖：`MarriageRequest` 输入 → `MarriageReply` 接受 → `LoverUpdate` 权威关系状态 → FairyGUI 使用的伴侣、地图、在线、天数与操作标签投影。
- 设备入口：沿用已合并的逍遥主界面“关系”入口证据；按用户本轮口径，双账号完整设备操作不再阻塞本任务。
