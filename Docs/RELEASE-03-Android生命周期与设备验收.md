# RELEASE-03 Android 生命周期与设备验收

## 验收口径

本轮按产品决定使用逍遥 Android 设备作为开发阶段实机等效设备，设备为 API 28、1600×900、6 GB 内存，并支持 arm64 翻译运行。实体手机特有的基带来电、真实蜂窝/Wi-Fi 射频切换、刘海与不同厂商输入法体验由项目所有者在发布物交付后自行观察，不再阻塞开发任务，也不得把未执行的实体手机步骤写成已执行。

容量验证继续使用真实协议模拟客户端维持目标连接数，不创建数百个实体账号或设备；RELEASE-03 不重复执行压力测试。

## 自动化与设备门禁

| 范围 | 开发阶段验收方式 | 通过条件 |
|---|---|---|
| arm64 四态 | Debug、Release、AOT+Trim、Trim-only 构建；正式签名态在等效设备安装启动 | 四态构建成功；正式态 Activity 保持 Resumed，无启动崩溃 |
| 安装与升级 | 新装正式 APK；同一 RSA 4096 密钥覆盖升级；应用数据标记复读 | 新装成功；覆盖升级成功且标记保留；不同签名覆盖被 Android 拒绝 |
| 签名资源更新 | 正式客户端请求 `bootstrap-package-index.signed.json`，逐包核对签名摘要，整版事务提交 | 未签名索引拒绝；全集未到齐不发布；成功后版本状态推进且队列清空 |
| 下载恢复 | 下载中强停/重启，复用 `.part` 与 Range；错误摘要拒绝 | 重启继续下载；坏包不进入 BundleInbox；不形成混合版本 |
| 空间不足 | 现有事务部署故障注入覆盖写入失败、回滚与无半成品 | 失败保持上一版本，启动恢复未完成事务 |
| 业务链 | 既有移动 UI/状态回归 + 等效设备连接正式协议服务端 | 登录、角色与主要业务入口无协议/启动崩溃；触控体验留实体手机观察 |
| 生命周期 | HOME/返回、锁屏/解锁、强停重启、网络断开/恢复、分辨率切换 | 恢复后 Activity 可重新进入前台；断网不崩溃；恢复后可重连 |
| API/内存/裁剪 | 最低 API 24 构建门禁；API 28 等效设备；Release AOT/Trim 启动 | minSdk=24；AOT 107/107；反射资源和 FairyGUI 启动无裁剪崩溃 |
| 密钥灾备 | 现有 `ReleaseSigningTool` 导出 AES-GCM 加密恢复包，在新路径导入为当前用户 DPAPI 后再次签名构建 | 错误恢复口令失败关闭；keystore 摘要一致；恢复材料可完成正式构建 |

## APK 密钥灾难恢复

恢复包不进 Git，必须保存到独立离线介质；恢复口令不得与恢复包同处。导出和导入都只从当前进程环境变量 `LYOCRYSTAL_ANDROID_RECOVERY_PASSPHRASE` 读取口令，并在工具进程内立即清除。包内 keystore 与 APK 口令使用 PBKDF2-SHA256（600000 次）派生的 256 位密钥和 AES-GCM 加密；随机 salt、nonce 与认证 tag 可公开随包保存。

```powershell
$env:LYOCRYSTAL_ANDROID_RECOVERY_PASSPHRASE = '<从受保护密码管理器临时读取>'
try {
  dotnet run --project Tools/ReleaseSigningTool/ReleaseSigningTool.csproj -c Release -- `
    export-android-recovery <keystore> <口令.dpapi> android-apk-2026 lyocrystal-release-2026 <离线恢复包>
} finally {
  Remove-Item Env:LYOCRYSTAL_ANDROID_RECOVERY_PASSPHRASE -ErrorAction SilentlyContinue
}
```

灾难恢复机使用相同口令执行 `import-android-recovery`，输出新的 keystore 与绑定恢复机 Windows CurrentUser 的口令 DPAPI 文件。工具拒绝覆盖任何既有目标；用途、alias、格式、认证 tag 任一不匹配即失败。恢复后必须使用输出材料完成一次 `publish-signed-android`，并用 `apksigner verify --verbose --print-certs` 对比原证书 SHA-256；只比较 keystore 文件摘要不算完整演练。

恢复包统一使用 `*.android-recovery.json` 扩展名并由 Git 忽略。恢复工具在最外层 `finally` 清除口令、DPAPI 明文、keystore 副本、恢复载荷和派生密钥；解析或第二输出失败也不得留下输出文件。

## 仍需项目所有者观察的实体手机体验

- 真实电话呼入/挂断与基带音频焦点；
- 蜂窝网络和 Wi-Fi 的真实射频切换；
- 刘海、圆角、厂商手势条与第三方输入法；
- 低端/推荐档实体设备的发热、电量、触控手感与长时间帧率。

这些是发布后的设备体验观察项，不会回写成开发阶段自动化“通过”。若发现缺陷，按发布后缺陷任务处理，不反向伪造本轮证据。
