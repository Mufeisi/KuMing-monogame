# OPS-BASIC-01 基础服务端监控与告警

## 可用工件

启用现有管理 HTTP 服务后，具备 `Operator` 或 `Administrator` 凭据的运营人员可读取：

```text
GET /operations/status
Authorization: Bearer <受保护的管理令牌>
```

返回 JSON 包含生成时间、指标是否启用、在线玩家数、Tick p95、保存 p95、当前网络队列深度、保存失败数、DB-03 备份状态与当前告警。端点继续复用 SEC-04 的来源 IP 限制、独立 Bearer 角色鉴权和 `ADMIN_AUDIT`，不增加公网入口或新凭据。

## 采集与并发边界

`BasicOperationsMonitor` 不维护第二套指标。在线数与队列深度来自主线程每秒写入的 PERF-00 gauge；Tick 和保存 p95 直接来自 PERF-00 全会话直方图；备份状态调用 DB-03 的线程安全快照。HTTP 和告警线程只读不可变快照，不直接遍历或修改玩家对象，继续满足玩家状态只由主线程写入的边界。

服务端管理 HTTP 启用时，即使没有设置 PERF-00 导出环境变量，也会启动名为 `server-operations` 的进程内低开销会话；关服时冻结并结束，不伪造压力测试数据，也不自动导出为性能基线。若显式启用了 PERF-00 环境变量，仍沿用原会话与导出路径。

## 告警策略

监控按状态转换写入不可静默丢弃的服务端 `Warn` 日志，格式以 `OPS_ALERT` 开头。某告警连续存在时只写一次 `triggered`，恢复时写一次 `recovered`，避免固定周期刷屏；写入失败不会提前提交告警状态，下个检查周期会重试。检查不可重入，关服会等待在途检查结束。基础版覆盖：

- PERF-00 未启用；
- Tick p95、保存 p95、当前网络队列深度超过配置阈值；
- 当前指标会话发生最终保存失败；
- SQLite 备份服务缺失、最近备份失败、首次备份长期卡在运行/空闲状态，或成功备份超过两倍配置周期未更新。

MySQL 部署不要求 SQLite 备份服务，不会产生 `backup-unavailable` 误报。默认阈值是发布前安全告警起点，不是容量承诺或压力验收结果；用户实机观察后可在 `Setup.ini` 的 `[Operations]` 调整：

| 配置 | 默认值 | 有效范围 |
|---|---:|---:|
| `TickP95WarningMilliseconds` | 100 | 1～60000 |
| `SaveP95WarningMilliseconds` | 30000 | 1～3600000 |
| `NetworkQueueWarningDepth` | 100 | 1～1000000 |
| `AlertCheckSeconds` | 10 | 1～3600 |

备份时效自动取 `SqliteBackupIntervalMinutes × 2`，不再维护重复配置。正式服启动会校验上述范围，非法值在管理服务启动前失败关闭。

## 范围边界

本任务只交付发布前基础 JSON 状态和日志告警，不实现图形仪表盘、地图分布、p99、自动上报、崩溃诊断、Kill Switch 或发布流水线。上述能力分别保留给 OPS-01、OPS-BASIC-02/03 和 RELEASE-02。

## 验证

专项测试覆盖快照组合、五类阈值/失败告警、状态转换去重、MySQL 备份非适用语义、真实 PERF-00 消费以及真实 `HttpListener` 的 Operator JSON 读取。完整命令与 TRX、构建日志见 `Docs/Evidence/GATE-P5/ops-basic-01-monitoring-20260810/`。
