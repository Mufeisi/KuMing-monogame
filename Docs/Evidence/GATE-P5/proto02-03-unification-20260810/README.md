# PROTO-02/03 协议统一与兼容矩阵证据

## 结论

- Android 正式构建不再编译 `Share/**/*.cs` 协议副本，直接链接 `Shared` 的协议事实源；只保留平台语言与 INI 适配文件。
- PC 与 Server 继续通过项目引用消费同一 `Shared`；Android Release 构建已验证该切换可编译。
- 自动生成清单覆盖 145 个客户端包、275 个服务端包、64 个公开枚举和 17 个协议源文件；源摘要统一 CRLF/LF 后计算，CI 的 `--verify` 在真实 LF 工作树副本中通过，实际漂移时返回失败。
- PROTO-03 兼容矩阵记录 PC `1.0.0`、Android `2.0.0`、`wire-v1` 与 SEC-06 签名资源最低版本的组合规则。

## 验证工件

阶段末归档以下原始输出：

- `proto02-03-targeted.trx`：生成清单、三端协议源接线、版本矩阵常量与 PROTO-01 golden 专项，14/14 通过。
- `proto02-03-base05-full.trx`：Base05 全量回归，348/348 通过。
- `protocol-manifest-verify.log`：Release 配置重新生成事实并执行无漂移校验。
- `android-host-build.log`、`android-shared-build.log`、`pc-build.log`、`server-library-build.log`：正式 Android 宿主及三端协议消费者构建结果。
- `cross-platform-proof.txt`、`artifact-version-proof.txt`：LF 检出验证、程序版本与 APK 哈希。
- `commands-and-exit-codes.txt`：精确命令和退出码。

## 构建结果

- Android Shared `net10.0-android` Release：2867 个既有警告，0 个错误。
- Android 正式宿主 `Client_MonoGame.Android` Release：2869 个既有警告，0 个错误；已生成 arm64 APK。
- PC `Client_VorticeDX11` Release：38 个既有警告，0 个错误。
- Server.Library Release：8 个既有警告，0 个错误。
- `git diff --check 3bd9403..HEAD`：通过。

这些验证使用模拟/构建接缝，不把真机体验纳入 PROTO-02/03；真机验收仍按用户后续实测安排进入 RELEASE-03。
