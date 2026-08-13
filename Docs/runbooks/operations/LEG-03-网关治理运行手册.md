# LEG-03 网关治理运行手册

- 状态：已接受
- 负责人：项目所有者
- 最后复核日期：2026-08-13
- 事实职责：网关治理查询、启用、回退和证据读取
- 产品规格：[`../../requirements/LEG-03-网关安全与运行诊断.md`](../../requirements/LEG-03-网关安全与运行诊断.md)
- 安全前置：[`../security/SEC-04-管理端安全配置.md`](../security/SEC-04-管理端安全配置.md)
- 取代关系：无

## 查询

使用 Operator 或 Administrator 的受保护令牌：

```http
GET /operations/gateway-governance
Authorization: Bearer <受保护令牌>
```

返回当前策略代次、模式、七类累计计数、当前跟踪会话数和最近 256 条违规证据。`clientReference` 是来源地址散列关联值，不是明文 IP。

## 先观察再执行

首次启动自动创建 `Configs/Operations/gateway-governance.json`，默认 `Observe`。至少覆盖一个正常玩家高峰窗口并复核分类计数、违规量和客户端行为后，才允许 Administrator 提交完整策略切换到 `Enforce`。禁止直接编辑运行中的文件；管理接口使用 `expectedRevision` 防止并发覆盖。

## 紧急回退

出现误杀时，Administrator 读取最新策略，将 `mode` 改为 `Disabled`，保留完整规则和最新 `expectedRevision` 后提交：

```http
POST /operations/gateway-governance/set
Authorization: Bearer <Administrator受保护令牌>
Content-Type: application/json
```

确认返回的新代次为 `Disabled`，再以正常玩家链路复测。配置写入失败时旧策略保持不变；文件损坏时服务端拒绝启动网络监听，禁止静默降级。

## 审计与人工复核

- 管理请求：服务日志中的 `ADMIN_AUDIT`。
- 策略变更：`GATEWAY_POLICY`，包含代次、模式和角色，不含原因原文。
- 违规证据：`GATEWAY_GOVERNANCE`，包含 UTC 时间、会话、来源散列、类别、阈值、观测值、窗口、响应和是否执行。
- `ManualBanReview` 只生成复核证据并断开当前会话，不自动写永久封禁；人工封禁仍走现有受控管理流程。
- 超大声明帧可能在操作系统关闭带未读正文的套接字时表现为 TCP 重置；运维以服务端会话移除和 `GATEWAY_GOVERNANCE` 证据为准，不以客户端是否收到断开原因包为判据。
