# TXT-12 Android 模拟器闭环证据

- 验证日期：2026-08-16
- 设备：Android Emulator `Medium_Phone_API_36.1`，API 36.1，x86_64，2400×1080，后台无窗口运行
- APK：`Client_MonoGame.Android`，Debug，`android-x64`
- APK 大小：109,783,114 字节
- APK SHA-256：`5CA25F2B4CFBBD865846C5EDF55A02F46A311766A219401FE4C55D6B5DCCE531`
- 操作方式：仅使用 `dotnet`、`adb` 和 Android 系统接口；未使用鼠标、键盘模拟或桌面 UI 自动化
- 验收口径：用户已确认模拟器验证即可，不要求物理设备复验

## 结果

APK 构建 0 错误、安装成功。模拟器通过既有 SmokeTest 配置自动注册测试账号、创建角色 `TxtNpc816` 并进入游戏。游戏端口使用 `adb reverse tcp:7000 tcp:7000` 映射，客户端仍连接回环地址 `127.0.0.2`，没有放宽 V1 明文协议禁止访问非回环地址的安全限制。

进入游戏后实际打开世界中的 `TXT灰度向导_验证`：

- `android-emulator-game-entered.png`：角色已进入游戏；
- `android-emulator-pilot-visible.png`：世界中可见灰度 NPC；
- `android-emulator-pilot-main.png`：真实 `[@MAIN]` 页面，中文、换行和内联按钮正常；
- `android-emulator-pilot-verified.png`：真实点击后进入 `[@VERIFY]`，显示“地图、怪物计数与会话变量闭环已完成；本试点不修改经济资产。”

运行时用量证据记录 `[@MAIN]` 分派 2 次、`[@VERIFY]` 分派 1 次；性能证据记录对应页面样本与耗时。文件见同目录的 `usage-latest.json` 和 `runtime-metrics-latest.json`。

## 测试隔离与恢复

正式候选采用 `CSharpFirst`。目标服已有精确 C# 登录处理器，因此 TXT `[@LOGIN]` 按设计被抑制，测试角色未获得候选脚本设置的 `P0=1`。为只验证 Android NPC 页面闭环，验证期间临时热更新一次 NPC 页，移除 `P0` 前置门槛；完成 `[@VERIFY]` 后立即恢复原文件。

- 临时版本 2 摘要：`988170E086613E8BDCB8AE510BC29FFBBFCDE321199CED09486ACFBF0E612159`；
- 恢复版本 3 摘要：`4968B79F650FBF20B16CCFAF4220099060EBA10E04872B58AEA2ECBD093EAD23`；
- 最终线上 NPC 文件 SHA-256 与仓库候选一致：`77C7EB1FF9E8FA6AC19A9BA04979B934E216A66AEFBC1A70D7088F77781C5979`。

验证后已停止应用、清除一次性测试口令环境属性、移除 ADB 端口反向映射，并把自动登录、自动注册、自动建角和自动进服全部恢复为关闭。测试账号与角色属于本次灰度产生的可识别测试数据，不混入候选脚本工件。

早期启动诊断截图继续保留以便追溯：`txt12-android-device-anr.png` 是修复前 Android 启动期同步 GPU 贴图预热导致的 ANR；`txt12-android-device-pass.png` 是修复后启动证据；`txt12-android-device.png` 是模拟器 System UI 遮罩，不作为通过结论。
