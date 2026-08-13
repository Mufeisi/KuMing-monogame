# OPS-BASIC-03 Kill Switch 运维说明

## 交付边界

正式服务端提供四个发布前总开关，默认均开启：

| 标识 | 关闭后的服务端行为 |
|---|---|
| `game-shop` | 不再下发商城列表，拒绝 GameShop 市场购买和正式商城购买。 |
| `resource-update` | `/api/file`、`/api/sound`、`/api/libheader`、`/api/libimage` 统一返回 `503`；`/api/health` 保持可用。 |
| `activities` | 拒绝新攻城活动；正在运行的攻城在下一次服务端主线程处理时停止且不做结算；魔龙进度、升降级与奖励处理停止。 |
| `high-risk-operations` | 拒绝开户、改密和删角；登录与普通游戏流程不受影响。 |

这是发布前基础总闸，不实现按商品、活动 ID、账号或灰度人群的细粒度规则。细粒度 Feature Flag 仍属于发布后的 OPS-03；外部签名仓库的事务化发布与回滚属于 RELEASE-02。

## 权限与调用

接口继续使用 SEC-04 的可信来源、Bearer 令牌和可靠审计：

- Operator/Administrator 查询：`GET /operations/kill-switches`
- 仅 Administrator 修改：`POST /operations/kill-switches/set`

请求体示例：

```json
{
  "feature": "game-shop",
  "enabled": false,
  "reason": "商城支付回执异常，紧急止损"
}
```

`feature` 只接受表中的四个标识；`enabled` 必填；`reason` 必须为 3～256 个字符。恢复时对同一标识提交 `enabled: true` 和新的恢复原因即可。

每次请求先写 SEC-04 的 `ADMIN_AUDIT`。每笔成功变更的完整审计记录与当前状态位于同一个原子文件中；远程查询会同时返回按代次连续的 `AuditTrail`。状态发布后另以不会静默丢弃的 `Warn` 写入便于检索的 `OPS_KILL_SWITCH` 副本，记录功能、目标状态、代次、角色和原因摘要，不记录 Bearer 令牌、来源 IP 明文或原因原文。即使日志目录瞬时故障，已原子提交的状态和完整审计仍保持一致，HTTP 不会伪报变更失败。

## 持久化与失败策略

状态位于 `Configs/Operations/kill-switches.json`。服务端首次启动时创建全开启状态；变更把新状态和连续审计历史一并写入同目录唯一 `.partial-*` 文件，刷新后再原子替换正式文件，成功后才向游戏线程发布不可变快照。因此持久化失败不会造成“接口说已关闭、进程内仍开启”的分裂状态，也不会出现已生效但无持久审计的变更。

正式状态缺字段、损坏、格式版本未知、连续审计重放结果与当前四闸不一致或不可读取时，服务端在启动监听和进入 Ready 前失败关闭，不会静默恢复全开启。运行时状态和半成品均由 `.gitignore` 排除，避免把现场开关误当发布默认值提交。

商城、活动和账户入口只在服务端主线程读取快照；管理 HTTP 线程不直接修改玩家、账户、活动或地图对象。正在运行的攻城收口也由下一次主线程 `Process` 完成。

## 验证范围

专项测试覆盖：默认与重启持久化、缺字段/损坏/未知版本失败关闭、商城/魔龙/开户真实入口、Operator 查询与越权拒绝、Administrator 真实 HTTP 修改、微端资源下载即时 `503` 以及健康检查保持可用。用户决定不做自动压力测试，容量与实机操作由后续现场验证承担。
