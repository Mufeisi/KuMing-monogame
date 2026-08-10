# PERF-01/02 模拟协议并发证据

## 口径

- 使用真实 TLS 监听、`SslStream`、`ClientVersion`、`Login`、`KeepAlive` 与现有 Packet 帧。
- 使用临时 SQLite；测试自动生成 100 个账号，不读取或修改生产数据库。
- 负载场景运行在独立 `dotnet test` 子进程中，避免 `Envir.Main`、Packet 方向和静态配置污染全量测试进程。
- 目标为 300 个同时在线协议连接，其中 100 个完成登录并持续心跳。
- 主动断开一个会话后，驱动器必须自动补足目标连接数。
- 稳定窗内逐一检查 100 个登录槽位的新增心跳数与末次成功时效，停止前再次确认 300 总连接和 100 登录连接仍在线。
- 这是协议/服务端短时并发验证，不代表 100 个地图内战斗角色、几百台真机或 24/72h soak。

## 复现命令

```powershell
$env:LYOCRYSTAL_LOAD_CONNECTIONS='300'
$env:LYOCRYSTAL_LOAD_ACTIVE='100'
$env:LYOCRYSTAL_LOAD_DURATION_SECONDS='30'
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release `
  --filter FullyQualifiedName~SimulatedProtocolLoadTests `
  --no-build --no-restore `
  --logger "trx;LogFileName=perf01-02-simulated-load.trx" `
  --results-directory Docs\Evidence\GATE-P4\perf01-02-simulated-load-20260810
```

## 结果

- 结果：1/1 通过，稳定窗口 30 秒，测试总耗时 1 分 4 秒。
- 峰值连接：300；登录成功：101（含主动掉线后补连）。
- 心跳响应：23197；心跳 p95：297.16ms。
- 建连重试：0；协议失败：0；补连：1。
- GC pause p95：39.32ms；网络队列高水位：300。
- 原始工件：`perf01-02-simulated-load.trx`。
- Base05 全量：345/345 通过，见 `perf01-02-base05-full.trx`。
- `Server.Library` Release：0 警告、0 错误，见 `server-library-build.log`。

本轮没有观察到需要以猜测性 ArrayPool、协议改写或渲染改造处理的阻断热点，因此 PERF-01/02 按“先测量、只修可复现瓶颈”口径关闭；真机角色动作与长时间稳定性由后续实机验收承担。
