# RELEASE-01 签名与密钥治理证据

## 工件

- `resource-signature-proof.txt`：两把生产资源公钥、轮换窗口、完整 261 包索引与双客户端版本验签结果。
- `apk-signature-proof.txt`：独立 RSA 4096 APK 签名证书、APK SHA-256 与 `apksigner` 结果。
- `android-production-signing-build.log`：本地 DPAPI 口令入口执行的 Android arm64 Release/AOT+Trim 签名构建原始输出，不含口令。
- `android-signing-command.txt`：签名构建与 `apksigner` 复验命令、退出码和最终公开摘要。
- `release01-targeted.trx`：T-07、防降级、生产公钥/完整索引/CI 分离专项。
- `release01-base05-full.trx`：Base05 全量回归。
- `release-signing-tool-build.log`、`server-library-build.log`、`pc-client-build.log`：最终提交对应 Release 构建输出。

## 结果

- T-07 与生产签名专项：11/11 通过。
- Base05 全量：351/351 通过。
- ReleaseSigningTool：0 警告、0 错误。
- Server.Library：0 警告、0 错误。
- PC 客户端：38 个既有警告、0 错误。
- Android arm64 Release/AOT+Trim 独立签名构建：成功；`apksigner` v2/v3 验证通过。

## 验收口径

- 资源签名私钥只存在于忽略的本地 DPAPI 文件或 CI Environment Secret，公钥编译进客户端信任表。
- APK keystore 与资源 PKCS#8 是三份不同材料；CI 固定时间比较摘要，相同即失败关闭。
- 资源签名索引覆盖随包索引全部 261 个包，PC `1.0.0` 与 Android `2.0.0` 均验签通过。
- APK 由独立 RSA 4096 证书签名，v2/v3 验证通过。
- 本地秘密、口令、keystore、私钥和环境 Secret 不进入证据或 Git。

CI 的 `production-signing` Environment 和 Secret 需要仓库管理员在 GitHub 外部配置；本提交交付失败关闭的工作流接缝，不伪称已保存任何远端秘密。
