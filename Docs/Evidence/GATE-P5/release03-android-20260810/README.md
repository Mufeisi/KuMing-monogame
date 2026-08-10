# RELEASE-03 Android 生命周期与设备验收证据

## 代码与自动化

- 正式远端索引固定为 `bootstrap-package-index.signed.json`，本地 baseline 继续使用未签名索引。
- 签名队列全集未到齐时禁止进入旧单包兼容路径；整版资源、版本快照和空队列仍由同一文件事务提交。
- `Mir2Config.ini` 只更新包内基线，不覆盖用户运行时服务器与仓库配置。
- `ReleaseSigningTool` 增加 AES-GCM 加密恢复包导出/导入；PBKDF2-SHA256 600000 次，错误口令、用途或 alias 失败关闭。

专项命令：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProductionReleaseSigningTests|FullyQualifiedName~Release02PipelineTests" --logger "trx;LogFileName=release03-targeted.trx" --results-directory Docs/Evidence/GATE-P5/release03-android-20260810
```

结果：14/14 通过。恢复测试真实启动签名工具子进程，覆盖错误口令失败和 DPAPI 往返。

## Android 工件与设备

- arm64 Debug（无 AOT/无 Trim）：构建通过，APK SHA-256 `A4F26AB6B661F49CDA8DB8C38239B508CE0AB06D54BFADA1F0ACDAB1B6D4F4EC`。
- arm64 Release（无 AOT/无 Trim）：构建通过，APK SHA-256 `3B11CB43E91EFC6C9F3A86EFAE5715D6805BFF0AB42ED6617BA03DE000B19F76`。
- arm64 Trim-only：构建通过，APK SHA-256 `8AEF291E60097C19AF6774D0FCA408E74137F63A79579BCD57541512E6E4A0F7`。
- 最终正式 arm64 AOT+Trim APK SHA-256：`958E3AA6325B4148FA651248FCC766C7053A979906301A231F54BC8B7984D16C`，AOT 107/107。
- `apksigner`：v2=true、v3=true、signers=1、RSA 4096；证书 SHA-256 为 `2d9f4b2e165407f7e781146f4cf0323aab9980b185c0002fd328ec89ec3bd670`。
- APK、keystore、DPAPI、恢复包均不进入 Git；仓库只保存构建日志、摘要和设备状态证据。
- 等效设备过程与边界见 `device-proof.txt`；最终运行态 JSON/日志见 `device-runtime-final/`。
- 账号自动注册、建角、进图和既有业务入口的设备证据复用 `Docs/Evidence/GATE-P1/runtime-20260809/`；本轮正式 APK 再次完成真实服务端 ClientVersion 握手，Base05 全量覆盖后续移动状态回归。未执行的实体手机触控/业务体验按项目所有者决定留交付后观察，不在本目录伪造新截图。

最终正式 AOT+Trim APK 再次覆盖安装成功，运行时 PackageRepo 和已提交资源版本保持不变。专项 TRX 14/14、Base05 全量 369/369；`ReleaseSigningTool` Release 构建 0 警告、0 错误。四态日志分别见本目录 `android-arm64-*-build.log`。
