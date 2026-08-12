# SEC-03 登录防护证据

## 范围与结论

- 任务：登录限流、失败指数退避、IP/账号双维度临时封禁。
- 结论：退出条件已满足。真实服务主线程 HTTP 登录脚本可分别跨来源地址触发账号封禁、跨账号触发 IP 封禁；PC 游戏登录入口复用同一策略并通过完整服务端构建与 Base05 回归。
- 不包含：SEC-02 C6、SEC-04～06、管理端与发布门禁。

## 实现摘要

- `Server.Security.LoginProtection` 统一维护账号/IP 两个维度：
  - 全部登录尝试窗口限流，默认账号 `30/60s`、IP `120/60s`；
  - 失败窗口默认 `300s`，账号第 `6` 次、IP 第 `20` 次失败触发封禁；
  - 指数退避默认从 `500ms` 起，最高 `30s`；
  - 账号/IP 封禁默认各 `15min`；
  - 成功登录清空失败退避，但保留当前尝试窗口，避免用成功请求绕过限流；
  - 跟踪状态容量分别限制为 20000 个账号、10000 个 IP，避免随机键导致内存无界增长。
- PC `Login` 与 `HTTPLogin` 均在现有服务主线程边界调用该策略；账号封禁写入现有 `Banned/BanReason/ExpiryDate` 并请求保存，IP 封禁写入既有 `IPBlocks`。
- `Setup.ini` 的 `Security` 节新增 10 个可配置项；读取时按安全下限收敛，未新增数据库表、线程或协议字段。

## 验证

执行：

```powershell
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --no-restore --logger "trx;LogFileName=sec03-base05.trx" --results-directory Docs\Evidence\GATE-P2\sec03-login-protection-20260809
```

结果：

- `LoginProtectionTests` 与 `Sec01LoginTransactionTests` 合并专项：13/13 通过。
- 基于已合入 SEC-02 C6 的最新主线复验：Base05 完整测试 238/238 通过，0 失败，0 跳过，用时约 62 秒。
- 机器可读结果：`sec03-base05.trx`。
- `git diff --check`：通过。

## 审查收口

- Standards 轴：无硬标准违规；保留的两个入口仅做投影，算法与状态集中在单一策略模块。
- Spec 轴首轮指出的“成功登录未限流”和“成功后 FIFO 键残留”均已修正；复审确认代码问题闭环。
- 复审最后要求的 README/TRX/PRD 可审计证据已在本目录与 PRD 当前快照补齐。

## 已知非本任务项

还原/构建仍报告仓库既有 NuGet 安全警告：`log4net 3.0.3` 与 `SQLitePCLRaw.lib.e_sqlite3 2.1.11`。本任务不顺手升级依赖，交由后续授权审计与依赖治理任务处理。
