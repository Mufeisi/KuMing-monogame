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

每个版本发布到 `releases/<ReleaseId>/`，目录内 `release-manifest.json` 固化工件路径、大小和 SHA-256。`channel-state.json` 以同目录临时文件写入、刷新并通过 `File.Replace/Move` 原子发布，保存当前版本、上一可运行版本、灰度比例和状态。整个 Prepare/Evaluate/Rollback 入口持有 `release-channel.lock` 独占锁，拒绝两个发布进程并发改指针。

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

执行评估：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools\Invoke-Release02.ps1 `
  -Action Evaluate -ChannelRoot C:\ReleaseChannels\LyoCrystal -MetricsPath C:\Ops\release-metrics.json
```

自动回滚条件固定为：

- 连续致命崩溃达到 3 次，不等待样本量；
- 更新尝试达到 100 后，更新失败率大于 2%；
- 启动达到 100 后，启动崩溃率大于 1%。

任一条件成立即原子交换 Current/Previous 指针并记录 `RolledBack`；指标未越线但任一口径不足 100 样本时仅记 `CanaryObserving`，保持 5% 灰度，不伪报健康或自动扩量。人工回滚使用 `-Action Rollback`，没有上一可运行版本时失败关闭。首次建立空渠道时没有可回滚目标，因此首个版本只能作为引导基线；至少再成功发布一个版本后，才允许把新版本暴露给真实灰度人群。

## 客户端文件事务

PC 与 Mono/Android 共用 `Shared.Release.TransactionalFileDeployment`：

1. 下载与签名哈希验证仍复用 SEC-06 既有链路；
2. 发布前复制并刷新所有旧目标文件，原子写入 Prepared/Applying 日志；
3. 每个新文件先写目标同目录 `.release-partial-*`，刷新后原子替换；
4. 全部文件发布后再次逐文件比较 SHA-256，再更新既有 Bootstrap 状态；
5. 任一步失败反向恢复旧文件、删除本次新增文件；启动时发现未完成日志会先恢复上一版本再继续。

PC 在 `PcBootstrapLayout.EnsureWritableDirectories` 恢复未完成事务，Mono/Android 在 `ClientResourceLayout.EnsureWritableResourceDirectories` 恢复；不创建第二套下载器、索引格式或协议。

## 边界

本任务只生成本地不可变渠道工件并验证灰度/回滚控制，不上传官网、商店或 CDN，不把本机 5% 状态解释为真实玩家流量。真机安装、覆盖升级、生命周期、网络切换、AOT/Trim 业务实操和 APK 密钥灾难恢复属于 RELEASE-03；用户决定开发阶段不组织几百真实账号或设备压测，协议容量继续采用已完成的模拟客户端验证。
