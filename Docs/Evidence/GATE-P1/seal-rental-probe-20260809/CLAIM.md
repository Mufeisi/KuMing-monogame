# P1-SEAL-RENTAL 封印租赁使用探针任务

- 任务：ANDROID-05 封印/租赁请求、响应、状态与 UI 投影冒烟。
- 状态：待审核；使用探针冒烟已通过。
- 分支：`codex/p1-seal-rental-probe-20260809`。
- 工作树：本地隔离工作树（路径已脱敏）。
- 基线：`71fb28c`。
- 文件所有权：`Tests/Base05.Tests/MobileSealRentalStateTests.cs` 与本证据目录。
- 做：复用真实封印/租赁协议类型和 `MobileSealRentalState` 公开 UI 投影。
- 不做：不改生产逻辑、协议、服务端、FairyGUI、数据库、物品数据或 PRD。
- 方法约束：必须复用现有状态/UI 接缝；禁止新增通用探针框架；同一问题两次无进展立即停止。
- 预估时间：不超过 40 分钟；超过 2 倍预算停止报告。
- 完成定义：使用探针与专项通过，证据归档，独立只读复核无阻塞。
- 语言：中文；代码标识符、命令和原始报错除外。

## 验收结果

- `MobileSealRentalStateTests`：31/31 通过，失败 0，跳过 0。
- 专用探针：`Seal_rental_usage_probe_smoke_projects_protocol_results_to_ui_values`。
- 封印链路：`CombineItem` 请求 → `CombineItem` 权威结果 → 选择清理与成功投影。
- 租赁链路：`BeginRentalRequest` 建立请求门控 → `ItemRentalRequest` 发包 → 同名服务端响应清除门控 → 会话、角色、伙伴名与操作状态投影。
- 设备入口：沿用已合并的逍遥主界面“封印/租赁”入口证据；真实物品交易环境不再阻塞本任务。
