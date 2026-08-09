# P1-VERIFY-B 模拟器验收证据（ANDROID-04～07）

## 范围与结论

- 任务：ANDROID-04 坐骑、ANDROID-05 物品封印/租赁、ANDROID-06 钓鱼、ANDROID-07 活动/赏金。
- 设备：`127.0.0.1:21503` Android 模拟器（ASUS_AI2401_A，API 28，x86_64），不是实体真机。
- 本次按 P1 当前允许范围只验证启动、登录页可达、前台→HOME→同一 Activity 恢复；模拟器结果不冒充真机门禁。
- Release APK 已安装且哈希与来源文件一致；启动页渲染正常，采集窗口内未见 `FATAL EXCEPTION`。
- ANDROID-04～07 **均阻塞，未通过**：应用停留登录页，无法进入角色/游戏场景。预登录日志明确报告本地 Bootstrap 版本索引连接失败及默认 FGUI 包探针缺失；同时没有可用测试服/Bootstrap 资源与已授权测试账号。本任务不补服务、不改配置、不猜账号、不伪造业务包。

## APK 与设备

详见 [`device-apk.txt`](device-apk.txt)：来源提交 `6229cdc` 的 Release `android-x64` APK，`versionName=2.0.0`、`versionCode=20000`、`minSdk=24`、`targetSdk=36`、SHA256 为 `531F6DDC88AAAEA5F5B5019B826EB8D2E7AE2C05F852B20860494014BBCBD80F`。当前分支基线为 `aaf5efea`；两者生产代码路径无差异，本次复用已构建 APK，未重复 AOT 构建。

## 启动与生命周期

- `01-login.png`：正确解析的打包组件启动后的登录页（ID/PASS 为空，未输入任何账号）。
- `05-login-ui.xml`：登录页最小 UI 层级摘要；`text`、`resource-id`、`content-desc`、任务标识等已脱敏。
- `home-flow.txt`：清理日志后未执行 `force-stop` 的前台→HOME→恢复命令记录；状态0 为我方 `MainActivity`，状态1 为模拟器 Launcher，状态2 恢复为同一 `MainActivity`。
- `02-before-home.png`、`03-home.png`、`04-after-resume.png`：对应状态0/1/2。`03-home.png` 仅作截图工件，Launcher 身份以 `home-flow.txt` 的 `dumpsys` 状态为准。
- `01-launch-status.json`：启动退出码、前台状态与截图退出码；生成 Activity 标识已脱敏。

## ANDROID-04～07 精确阻塞

| 功能 | 当前可达位置 | 观察到的阻塞 | 结论 |
|---|---|---|---|
| ANDROID-04 坐骑 | 登录页 | `PreLoginUpdate` 无法连接 `http://127.0.0.1:7777/api/file/Packages/bootstrap-package-index.json`；无测试服/Bootstrap 资源与授权角色 | 阻塞，未验收 |
| ANDROID-05 物品封印/租赁 | 登录页 | 同上；无法进入角色场景，不能猜账号或伪造封印/租赁业务闭环 | 阻塞，未验收 |
| ANDROID-06 钓鱼 | 登录页 | 同上；无法进入角色场景，不能猜账号或伪造钓鱼业务闭环 | 阻塞，未验收 |
| ANDROID-07 活动/赏金 | 登录页 | 同上；无法进入角色场景，不能猜账号或伪造活动/赏金业务闭环 | 阻塞，未验收 |

阻塞日志的最小筛选结果见 [`bootstrap-filtered-20260809-0106.txt`](bootstrap-filtered-20260809-0106.txt)：默认 FGUI 包仍缺少 `BaseRes_fui.bytes`、`UIRes_fui.bytes`、`Font_fui.bytes` 等探针。代码自动化测试、APK 可安装或登录页截图均不替代四项业务的真机闭环。

## 过滤与敏感信息处理

- [`lifecycle-filtered-20260809-0103.txt`](lifecycle-filtered-20260809-0103.txt) 仅保留我方包生命周期/FATAL 结果，FATAL 匹配数为 0；未归档原始全量 logcat。
- [`bootstrap-filtered-20260809-0106.txt`](bootstrap-filtered-20260809-0106.txt) 仅保留预登录资源阻塞行；进程/线程标识、生成 Activity 类标识均已脱敏。
- 证据目录未保存 `device_id`、`appkey`、`pairUUID`、session、token、password、第三方遥测或其他长标识；截图未输入账号/密码。
- 未修改生产代码、测试代码、服务器配置、账号、PRD 或其他会话的证据目录。
