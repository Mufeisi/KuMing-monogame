# LEG-03 网关安全与运行诊断完成证据

- 日期：2026-08-13
- 分支：`codex/leg-03-gateway-governance`
- 范围：七类流量治理、受控配置、主线程处置、结构化证据和性能门禁
- 语言：中文，代码标识符、命令和原始测试字段除外

## 工件

1. `src/Server/Server/Operations/GatewayTrafficGovernance.cs`：观察、执行、关闭三种模式，七类窗口计数、五级响应、原子策略与有界证据。
2. `src/Server/Server/MirNetwork/MirConnection.cs`：按完整协议帧判定超大封包，在服务端主循环分类和处置玩家动作。
3. `src/Server/Server/Utils/HttpServer.cs`：沿用 SEC-04 鉴权的查询和修改接口。
4. `Tests/Base05.Tests/GatewayTrafficGovernanceTests.cs`：领域、权限、HTTP、原子持久化、失败关闭和性能回归。
5. `Tests/Base05.Tests/ServerLifecycleSmokeTests.cs` 与 `SimulatedProtocolLoadTests.cs`：真实 TCP 分段超大帧，以及 12 会话正常协议负载。

## 验证结果

```text
Server.Library Release build: 通过，0 错误，8 条既有警告
Server.MirForms Release build: 通过，0 错误，446 条既有项目警告
LEG-03 联合门禁: 通过 32，失败 0
LEG-03 核心门禁最终复跑: 通过 18，失败 0
真实服务生命周期组合: 通过 13，失败 0
Base05.Tests 最终全量: 通过 408，失败 0，跳过 0
```

前两次全量均暴露 `真实Server回环V1路径完成KeepAlive` 准入失败。根因是网络测试中的现有连接构造会写入全局回环地址短期封禁，而测试夹具没有恢复该全局状态；新增超大帧用例使执行顺序更稳定地触发了污染。修复方式是夹具进入时移除并保存 `127.0.0.1` 封禁，退出时精确恢复原值，没有放宽业务断言。修复后 13 项真实服务生命周期组合和 408 项最终全量全部通过。

测试结果文件：

- `Tests/Base05.Tests/TestResults/leg03-gate.trx`
- `Tests/Base05.Tests/TestResults/leg03-server-lifecycle-fixed.trx`
- `Tests/Base05.Tests/TestResults/leg03-full-final.trx`

## 性能与正常负载

同进程中位数对比：

```text
LEG03_PERFORMANCE disabledNs=72.6 observeNs=2586.3 addedUs=2.514 limitUs=5.000
```

当前源码 12 会话协议负载结果：

```text
SIMULATED_LOAD_RESULT target=12 active=4 peak=12 logins=5 keepAliveReplies=136 replenishments=1 connectionRetries=0 protocolFailures=0 keepAliveP95Ms=53.62 tickP95Ms=0.00 gcPauseP95Ms=5.73 queueHighWater=11
```

默认 `Observe` 下未处置正常会话；分段发送的超大声明帧在真实 TCP 回环中产生一次结构化证据，并由主循环移除会话。

## 安全与回滚

- 审计使用会话号和地址散列，不保存 IP 明文、凭据或聊天正文。
- 配置损坏、未知格式或非法阈值时拒绝创建治理服务，生产启动顺序保证不先开放网络监听。
- 紧急回退通过受控管理接口将模式切换为 `Disabled`；代码回滚本任务提交即可完全移除能力，无数据库或协议迁移。
