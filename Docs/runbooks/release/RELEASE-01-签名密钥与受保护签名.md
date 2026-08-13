# RELEASE-01 签名密钥与受保护签名

## 结论与边界

APK 与资源索引使用两套不可互换的生产密钥：Android APK 使用独立 RSA 4096 keystore；资源索引使用 ECDSA P-256、SHA-256、P1363。资源私钥不进入客户端、APK 或 Git，客户端只编译 SPKI 公钥。APK keystore、口令和资源 PKCS#8 私钥均不得写入普通配置、命令行、日志或发布物。

本任务完成签名实现、当前/下一资源公钥、轮换窗口、受保护的本地签名入口和 CI 短暂取用接缝。签名工件的事务发布、灰度和回滚属于 `RELEASE-02`；APK 密钥的异地灾难恢复演练属于 `RELEASE-03`。

## 生产密钥布局

| 用途 | Key ID / Alias | 算法 | 受保护载体 | 公开内容 |
|---|---|---|---|---|
| 当前资源索引 | `resource-2026-a` | ECDSA P-256 | 本地 DPAPI CurrentUser 或 CI Environment Secret | `Docs/ReleaseKeys/resource-2026-a.public.json` |
| 下一资源索引 | `resource-2026-b` | ECDSA P-256 | 本地 DPAPI CurrentUser 或 CI Environment Secret | `Docs/ReleaseKeys/resource-2026-b.public.json` |
| Android APK | `lyocrystal-release-2026` | RSA 4096 | 独立 keystore；口令用本地 DPAPI CurrentUser 或 CI Environment Secret | APK 签名证书 |

本地受保护文件固定放在 `Configs/ReleaseSecrets/`；该目录、`*.dpapi`、`*.keystore` 与 `*.jks` 均由 Git 忽略。DPAPI `CurrentUser` 只允许生成文件的同一 Windows 账号解密，因此发布账号迁移前必须先完成 `RELEASE-03` 的密钥灾难恢复流程，不能复制 DPAPI 文件后假定另一账号可用。

## 本地资源签名

首次生成当前/下一资源密钥时执行一次，输出路径已存在会拒绝覆盖：

```powershell
dotnet run --project Tools/ReleaseSigningTool/ReleaseSigningTool.csproj -c Release -- provision-resource-key resource-2026-a Configs/ReleaseSecrets/resource-2026-a.pkcs8.dpapi Docs/ReleaseKeys/resource-2026-a.public.json
dotnet run --project Tools/ReleaseSigningTool/ReleaseSigningTool.csproj -c Release -- provision-resource-key resource-2026-b Configs/ReleaseSecrets/resource-2026-b.pkcs8.dpapi Docs/ReleaseKeys/resource-2026-b.public.json
```

生成公钥后必须把 SPKI 与序列窗口编译进 `BootstrapManifestTrustConfiguration.TrustedKeys`，再发布同时信任当前/下一密钥的客户端。签名与复验：

```powershell
dotnet run --project Tools/ReleaseSigningTool/ReleaseSigningTool.csproj -c Release -- sign-resource-index src/Clients/Client_MonoGame.Shared/BootstrapAssets/bootstrap-package-index.json Docs/ReleaseKeys/bootstrap-package-index.signed.json resource-2026-a 1 1.0.0 Configs/ReleaseSecrets/resource-2026-a.pkcs8.dpapi
dotnet run --project Tools/ReleaseSigningTool/ReleaseSigningTool.csproj -c Release -- verify-resource-index Docs/ReleaseKeys/bootstrap-package-index.signed.json 1.0.0
```

签名工具严格导入完整 P-256 PKCS#8，构造 SEC-06 确定性载荷，签名后立即自验；未知 Key ID、错误序列窗口、清单篡改或最低版本不满足均失败关闭。输出只包含 Key ID、序列和包数量。

## 本地 APK 签名

把一次性口令放入当前 PowerShell 环境变量；工具读取后会清除子进程副本，调用脚本必须在 `finally` 中清除父 PowerShell 副本：

```powershell
$env:LYOCRYSTAL_APK_PASSWORD = '<一次性输入>'
try {
    dotnet run --project Tools/ReleaseSigningTool/ReleaseSigningTool.csproj -c Release -- protect-environment-secret android-apk-2026 LYOCRYSTAL_APK_PASSWORD Configs/ReleaseSecrets/lyocrystal-android-2026-r2-password.dpapi
    if ($LASTEXITCODE -ne 0) { throw "保护 APK 口令失败，退出码 $LASTEXITCODE" }
}
finally {
    Remove-Item Env:LYOCRYSTAL_APK_PASSWORD -ErrorAction SilentlyContinue
}
```

签名构建由工具在子进程环境中传递口令，命令行和构建日志不出现口令：

```powershell
dotnet run --project Tools/ReleaseSigningTool/ReleaseSigningTool.csproj -c Release -- publish-signed-android src/Clients/Client_MonoGame.Android/Client_MonoGame.Android.csproj Configs/ReleaseSecrets/lyocrystal-android-2026-r2.keystore Configs/ReleaseSecrets/lyocrystal-android-2026-r2-password.dpapi android-apk-2026 lyocrystal-release-2026 Docs/Evidence/GATE-P5/release01-signing-20260810/android-production-signing-build.log
```

发布前必须再用 Android SDK `apksigner verify --verbose --print-certs` 验证 APK；资源索引密钥不得被导入 APK keystore，APK keystore 也不得传给资源签名命令。

## CI 受保护签名

`.github/workflows/release-01-signing.yml` 绑定 GitHub Environment `production-signing`。仓库管理员应在该 Environment 配置审批规则和以下 Secret；缺少任一项时流水线失败关闭：

- `LYOCRYSTAL_ANDROID_KEYSTORE_BASE64`
- `LYOCRYSTAL_ANDROID_STORE_PASSWORD`
- `LYOCRYSTAL_ANDROID_KEY_PASSWORD`
- `LYOCRYSTAL_ANDROID_KEY_ALIAS`
- `LYOCRYSTAL_RESOURCE_KEY_A_PKCS8_BASE64`
- `LYOCRYSTAL_RESOURCE_KEY_B_PKCS8_BASE64`

流水线在签名前比较三份密钥材料摘要，任何两份相同即拒绝；所选资源私钥只进入对应签名步骤，APK 口令只进入 Android 发布步骤。临时 keystore 与签名索引在 `always()` 清理，工件只保留 7 天。Secrets 的实际配置属于仓库管理员的外部权限动作，代码仓库不保存也不声称已经配置这些值。

## 轮换与事故处理

1. 当前客户端同时信任 `resource-2026-a`（序列 1～999999）和 `resource-2026-b`（从 900000 开始）。
2. 在序列达到 900000 前完成双公钥客户端覆盖，再切换到 `resource-2026-b` 签名。
3. 不复用序列；每次发布使用高于已接受状态的新序列。
4. 旧密钥到期后发布收紧或移除旧窗口的客户端；远端索引无权增加受信任公钥。
5. 若资源私钥泄露，停止发布、关闭资源更新 Kill Switch、发布收紧信任表的客户端，再用下一密钥恢复。
6. 若 APK keystore 泄露，停止 APK 发布并按渠道密钥恢复规则处理；不得用资源密钥替代 APK 密钥。

## RELEASE-01 验收

- T-07 覆盖正确签名、篡改、未知/过期密钥、轮换窗口、低序列、同序列异载荷、最低版本、防降级状态和签名包哈希。
- 生产签名索引必须与随包未签名索引的全部包名、SHA-256 和大小逐项一致。
- PC 兼容版本 `1.0.0` 与 Android 兼容版本 `2.0.0` 均可验证生产索引。
- APK 必须由独立 RSA 4096 证书签名，资源公钥必须是两把不同的 ECDSA P-256 公钥。
- `git ls-files '*.dpapi' '*.keystore' '*.jks'` 必须为空。
