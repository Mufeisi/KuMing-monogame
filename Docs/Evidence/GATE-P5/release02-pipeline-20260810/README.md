# RELEASE-02 一键发布与事务回滚证据

## 工件

- `release02-targeted.trx`：文件事务与灰度/自动回滚专项。
- `release02-base05-full.trx`：Base05 全量回归。
- `shared-build.log`、`mono-shared-build.log`、`pc-client-build.log`：三个正式消费者的 Release 构建输出。
- `android-signing-build.log`：最终一键发布产生的 Android arm64 AOT+Trim 独立签名构建日志。
- `apk-signature-proof.txt`：真实 `apksigner.bat` 的 v2/v3、单签名者、RSA 4096 输出。
- `artifact-proof.txt`：脚本外独立复算的渠道指针、工件数量、261 包摘要和 `.partial` 残留计数。
- `release-run-transcript.txt`：最终 r3 一键运行的完整原始 transcript，并作为发布清单中的受校验工件。
- `gateway-proof.txt`：实际 loopback HTTP 网关的 ClientId 分桶、5% 当前版本选择、幂等事件接收与指标状态。
- `one-click-command.txt`：最终一键命令、退出码和公开结果摘要。
- `channel-state.json`、`channel-metrics.json`、`release-manifest.json`：最终本地渠道状态、真实事件指标与不可变版本工件清单副本。

## 最终一键运行

- ReleaseId：`release02-20260810-r3`；Sequence：4；灰度：5%。
- 上一可运行版本：`release02-20260810-r2`。
- Base05：366/366；RELEASE-02 专项 15/15。
- 资源导出：261/261；签名索引正式信任表复验通过。
- Android：arm64 Release/AOT+Trim 独立签名完成；APK v2/v3、单签名者、RSA 4096。
- 发布目录文件清单：1097 项（含本次完整 transcript），脚本外复算 0 缺失、0 哈希/大小不一致。
- 签名资源包：261 项，脚本外复算 0 缺失、0 哈希/大小不一致。
- 发布目录总大小：1,429,612,709 字节；残留 `.partial-*` 目录：0。
- 最终发布网关 PID 18700 实际监听 `127.0.0.1:18443`；`proof-canary-9` 的稳定桶为 4，实际返回 r3，并从响应的 `ResourceRepositoryPath` 下载签名索引 40,851 字节；重复 HealthyLaunch EventId 未重复累计。

本地渠道路径在 Git 外，仅归档不含秘密的状态、清单和验证输出。未向官网、商店、CDN 或真实玩家发布。
