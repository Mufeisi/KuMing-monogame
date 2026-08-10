# RELEASE-02 事务发布与灰度回滚

## 一键发布

唯一入口为 `Tools/Invoke-Release02.ps1`。`Prepare` 在同一渠道锁内依次执行 Base05 全量冒烟、PC/服务端 Release 发布、261 个资源包导出、资源索引签名与正式信任表复验、Android arm64 AOT+Trim 独立签名构建、`apksigner` 证书复验、全部工件 SHA-256 清单生成，最后才把完整暂存目录原子移动为不可变版本目录并将渠道切到 5% 灰度。任一步失败都会删除未发布的 `.partial-*` 目录，不修改渠道指针。

```powershell
$env:ANDROID_HOME = 'C:\Users\luo\AppData\Local\Android\Sdk'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools\Invoke-Release02.ps1 `
  -Action Prepare `
  -ChannelRoot C:\ReleaseChannels\LyoCrystal `
  -ReleaseId release-2026-08-10 `
  -Sequence 3 `
  -MinimumClientVersion 1.0.0
```

受保护的资源私钥、APK keystore 和 APK 口令继续使用 RELEASE-01 的 `Configs/ReleaseSecrets/` DPAPI/忽略目录，不进入发布清单、日志或 Git。若系统 Java 不是 JDK 17 或更高版本，通过 `-JavaHome` 显式指定；入口只允许执行 `apksigner.bat/.cmd/.exe`，不会把 JAR 文件关联误当成验签成功。

## 渠道状态与回滚

每个版本发布到 `releases/<ReleaseId>/`，目录内 `release-manifest.json` 固化工件路径、大小和 SHA-256。`channel-state.json` 以同目录临时文件写入、刷新并通过 `File.Replace/Move` 原子发布，保存当前版本、上一可运行版本、失败版本、灰度比例和状态。整个 Prepare/Evaluate/Record/Rollback 入口持有 `release-channel.lock` 独占锁，拒绝两个发布进程并发改指针。Prepare 默认启动仅监听 `127.0.0.1` 的渠道网关；外部 TLS 入口可把 `/release/select` 和 `/release/events` 反向代理到该 loopback 服务。

客户端或下载入口用稳定且不含账号明文的 ClientId 请求 `GET /release/select?clientId=...`。网关对 ClientId 做 SHA-256 确定性分桶：5 个桶返回当前灰度版本，其余返回上一健康版本，并在返回前验证所选不可变版本的完整发布清单。响应给出 `ArtifactBasePath` 与 `ResourceRepositoryPath`；同一网关的 `/releases/<ReleaseId>/...` 只在完整性验证后提供真实文件下载。相同 ClientId 始终得到相同选择，不靠人工改 JSON 模拟 5%。

指标文件格式为 `lyocrystal-release-metrics-v1`，且 `ReleaseId` 必须等于当前灰度版本：

```json
{
  "Format": "lyocrystal-release-metrics-v1",
  "ReleaseId": "release-2026-08-10",
  "UpdateAttempts": 100,
  "UpdateFailures": 0,
  "Launches": 100,
  "Crashes": 0,
  "ConsecutiveFatalCrashes": 0
}
```

OPS-BASIC-01/02 的本地采集或 TLS 反向代理将幂等事件提交到 `POST /release/events`；事件类型为 UpdateAttempt、UpdateFailure、Launch、Crash、FatalCrash 或 HealthyLaunch。网关在同一渠道锁内原子累计 `channel-metrics.json`，每接受一个新 EventId 就立即执行评估，无需人工轮询。`-Action Evaluate` 只保留给离线重放与灾难演练：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools\Invoke-Release02.ps1 `
  -Action Evaluate -ChannelRoot C:\ReleaseChannels\LyoCrystal -MetricsPath C:\Ops\release-metrics.json
```

自动回滚条件固定为：

- 连续致命崩溃达到 3 次，不等待样本量；
- 更新尝试达到 100 后，更新失败率大于 2%；
- 启动达到 100 后，启动崩溃率大于 1%。

任一条件成立时，先逐项验证上一版本清单、大小、SHA-256 和签名资源索引，再把 Current 单向切回 Previous，把坏版本记入 FailedReleaseId，并清空 Previous；不会把坏版本交换成下一次回滚目标。`RolledBack` 状态下重复回滚直接拒绝。指标未越线但任一口径不足 100 样本时仅记 `CanaryObserving`，保持 5% 灰度，不伪报健康或自动扩量。

## 客户端文件事务

PC 与 Mono/Android 共用 `Shared.Release.TransactionalFileDeployment`：

1. 下载与签名哈希验证仍复用 SEC-06 既有链路；
2. PC 先下载、验签并解压签名队列全部包；Android 等待该签名队列的全部 Bundle 到齐；任何一包缺失都不开始发布；
3. 发布前复制并刷新整版所有旧目标文件，原子写入 Prepared/Applying 日志；事务根持有跨进程 `FileShare.None` 锁，恢复器不会碰触另一进程正在提交的事务；
4. 每个新文件先写目标同目录 `.release-partial-*`，刷新后原子替换；PC 的版本快照与更新队列也属于同一事务；
5. 全部文件发布后再次逐文件比较 SHA-256，提交后才刷新 Android 派生状态；
6. 任一步失败反向恢复旧文件、删除本次新增文件；启动时发现未完成日志会先恢复上一版本再继续。

PC 在 `PcBootstrapLayout.EnsureWritableDirectories` 恢复未完成事务，Mono/Android 在 `ClientResourceLayout.EnsureWritableResourceDirectories` 恢复；不创建第二套下载器、索引格式或协议。

## 边界

本任务生成本地不可变渠道工件、可被 TLS 反向代理使用的 loopback 发布网关和自动指标回滚闭环；没有上传官网、商店或 CDN，也没有真实玩家流量。真机安装、覆盖升级、生命周期、网络切换、AOT/Trim 业务实操和 APK 密钥灾难恢复属于 RELEASE-03；用户决定开发阶段不组织几百真实账号或设备压测，协议容量继续采用已完成的模拟客户端验证。
