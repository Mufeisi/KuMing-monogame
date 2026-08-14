# GUI-13 签名包启动激活与测试服外环证据

- 执行日期：2026-08-14
- 切片：`GUI-13`
- 分支：`codex/leg-07-gui-startup-activation`
- 语言：中文

## 任务简报

- 目标：让 PC 游戏子进程与 Android 正常启动恢复上一已接受签名 GUI 包，并以隐藏测试服完成真实协议打开、非法拒绝、合法兑换、关闭、移动和退出。
- 做：复用 Bootstrap 接受状态、签名清单、主备下载和原子安装接缝；增加跨 `core-startup/custom-gui` 的原子激活存储；接回两端运行时；完成外部测试服冒烟。
- 不做：不增加信任根、第二下载器或第二协议；不改变数据库 Schema；不进入 LEG-08；不使用可见桌面自动化。
- 方法约束：只有两个已验签包均就绪才切换当前版本；任一失败保留上一版本；测试服务仅允许 `TestServer=true` 的隐藏限时入口。
- 完成定义：两端启动消费路径有公共行为测试，真实测试服外环完成且业务事实落盘，所有测试、构建、差异和清理门禁通过。

## 交付工件

1. `CustomGuiAcceptedReleaseStore` 从既有 Bootstrap 接受状态读取原始已签名清单，逐包复核名称、大小和摘要；`core-startup` 与 `custom-gui` 只进入同序列 pending，两包齐全且可由生产加载器读取后才原子切换 `current.txt`。
2. PC 预登录与 Android Bootstrap 正常更新队列都把两包列为必需包；PC `PcCustomGuiRuntime` 和 Android `FairyGuiHost` 在首次动态窗口打开时从当前已接受版本恢复，不再依赖测试代码显式注册。
3. 失败测试确认单包不会激活、损坏第二包不会替换旧版本、上一已接受版本仍可读取；PC 与 Android 主源失败转备源后均能激活并由真实运行时消费。
4. 服务端增加仅测试服可用的隐藏限时入口，沿用正常资源、脚本、网络、HTTP 和保存路径；动态 .NET 运行时不再误注册 NativeAOT 专用 WinForms COM 接缝，监听关闭回调在停止竞争下保持空值安全。
5. PC 零进度状态不再创建零宽渲染目标：填充宽度为零时隐藏控件并保留合法最小纹理尺寸；定向测试与修复后真实外环均未再出现 Direct3D `InvalidParameter`。

## 真实测试服外环

测试服使用既有 SQLite 数据、地图和脚本，服务端与 PC 客户端均以隐藏窗口启动。测试账号在运行前精确恢复为 `Gold=5000`、`Credit=0`、无 `Flag[1998]`；客户端进入地图 0 后由服务端打开 `activity.exchange@1`：

1. 客户端先提交不存在的兑换项，服务端返回 `Rejected`，未修改玩家事实。
2. 客户端选择登记的活动项并提交，服务端返回 `Accepted`，权威事务扣除 1000 金币、增加 10 信用点并设置 `Flag[1998]=1`。
3. 客户端发送登记的关闭动作，收到服务端关闭包；随后从 `288,614` 移动到 `288,613` 并正常退出。
4. 限时服务端自行停止并保存；全新 SQLite 连接复核 `Gold=4000`、`Credit=10`、`Flag[1998]=1`、坐标 `288,613`。
5. 7000/7777 监听均已关闭，外部测试服临时 `CustomGuiPackageSequence` 覆盖已移除；运行摘要见 [`GUI-13-protocol.log`](GUI-13-protocol.log)。

GUI-06 已保存同一生产签名加载器和双端 Adapter 的真实帧；本切片按仓库禁止抢焦点约束只执行隐藏外环，不重复制造可见桌面截图。既有帧仍见 [`GUI-06-PC-1280x720.png`](GUI-06-PC-1280x720.png) 与 [`GUI-06-Android-1280x720.png`](GUI-06-Android-1280x720.png)。

## 测试与门禁

| 门禁 | 结果 |
|---|---|
| Base05 Release 全量 | 490/490 通过；结果文件 `Tests/Base05.Tests/TestResults/gui13-base05.trx` |
| Launcher/PC/移动 Windows Release 全量 | 124/124 通过；结果文件 `eng/WindowsIntegration/Launcher.PlayerShellIntegration/TestResults/gui13-windows.trx` |
| Shared Release 构建 | 0 警告、0 错误 |
| Server Release 构建 | 0 警告、0 错误 |
| PC Release 构建 | 0 错误；保留既有 WindowsBase 版本冲突警告 |
| Android Release 构建 | 0 错误；保留既有 XML 注释、可空性等警告 |
| 隐藏测试服真实外环 | 打开、非法拒绝、合法兑换、关闭、移动、退出、保存和停机全部通过 |
| `git diff --check main` | 通过；仅 Git 换行提示 |

## 回滚与范围

回滚本切片不会删除已激活版本目录；客户端可把 `current.txt` 原子切回上一已接受签名序列。服务端先关闭 `Activities` Kill Switch 并使现有窗口失效，再回滚代码；测试服业务样例使用既有金币、信用点和角色标记，不涉及 Schema 回滚。

本切片只关闭 `GUI-13` 与 `GATE-GUI-DYNAMIC`，没有扩张到商城、活动平台、通用脚本 UI 或 LEG-08。代码、签名激活目录、真实协议日志、持久化结果、TRX 和 Release 构建均为可观察工件，工件数量高于过程文档数量；交流、证据、状态和提交信息全部使用中文。
