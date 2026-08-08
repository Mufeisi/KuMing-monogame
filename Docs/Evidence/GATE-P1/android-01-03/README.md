# P1-VERIFY-A 模拟器验收证据（ANDROID-01～03）

## 范围与结论

- 任务：ANDROID-01 商城、ANDROID-02 师徒（Mentor/Mentee）、ANDROID-03 关系（Marriage 系）。
- 验收设备：`127.0.0.1:21503`，Android 模拟器/虚拟设备；**不是实体真机**。
- 本次仅做模拟器可达的基本启动验收，不把模拟器结果冒充真机门禁。
- 基本启动、登录页显示、前台→HOME→同一 Activity 恢复：在明确采集窗口内完成（见 `02-login.png`、`07-before-home.png`、`08-home.png`、`09-after-resume.png` 与 `foreground-home-flow.txt`）。`08-home.png` 近乎空白，**不作为 Launcher 视觉证据**；Launcher 身份以 `dumpsys` 的 `mResumedActivity` 为准。
- ANDROID-01～03 业务闭环：**阻塞，未通过**。当前没有可用测试服/Bootstrap 资源和已授权测试账号，无法进入角色/游戏场景；未猜测账号、未启动临时服务、未修改服务器或配置。

## APK 与设备

- 源码基线：`6229cdc`（工作树 `codex/p1-verify-a-20260809`）。
- 构建命令：

  `dotnet publish Client_MonoGame.Android/Client_MonoGame.Android.csproj -f net10.0-android -c Release -r android-x64 -p:MobileBootstrapAssetMode=Micro -p:AndroidPackageFormat=apk -p:ArchiveOnBuild=false -p:RunAOTCompilation=true -p:PublishTrimmed=true -v:minimal`

- 构建结果：退出码 0；既有 FairyGUI/XML 注释与 Android 16 页面大小警告不阻断发布。
- APK：`Client_MonoGame.Android/bin/Release/net10.0-android/android-x64/publish/com.lommir.client.monogame.android-Signed.apk`
- APK SHA256：`531F6DDC88AAAEA5F5B5019B826EB8D2E7AE2C05F852B20860494014BBCBD80F`
- APK 元数据：包名 `com.lommir.client.monogame.android`，`versionCode=20000`，`versionName=2.0.0`，`minSdk=24`，`targetSdk=36`，ABI `x86_64`。
- 模拟器：型号 `ASUS_AI2401_A`，API `28`，系统 `9`，ABI 列表 `x86_64,arm64-v8a,x86,armeabi-v7a,armeabi`。
- 安装：`adb install -r -d` 返回 `Success`。

## 启动与基本操作

- 解析后的 Android 启动组件为 `com.lommir.client.monogame.android/crc[REDACTED].MainActivity`（生成类标识已脱敏）。
- 首次使用源码类名直接启动会得到 `Error type 3`（类名不是打包后的组件名）；改用 `resolve-activity` 返回的组件后启动成功。
- 前台启动后 `mResumedActivity` 为上述 MainActivity；在不 force-stop 的前台→HOME→恢复流程中，HOME 后为模拟器 Launcher，再次 `am start` 恢复上述 MainActivity。
- `lifecycle-filtered-20260809-0048.txt` 是新采集窗口的最小筛选输出；筛选结果中我方包 FATAL 匹配数为 0，结论限定为**该采集窗口内未见立即崩溃**，不作全局稳定性结论。
- `PreLoginUpdate` 明确报告：`http://127.0.0.1:7777/api/file/Packages/bootstrap-package-index.json` 连接失败，且默认 FGUI 包资源探针未就绪。这是外部资源/测试服缺失的可复现阻塞，不在本任务内补服务或改配置。

## ANDROID-01～03 精确阻塞

| 功能 | 当前可达位置 | 阻塞原因 | 结论 |
|---|---|---|---|
| ANDROID-01 商城 | 登录页 | 没有可用测试服/Bootstrap 资源，且没有授权测试账号与角色，无法进入 GameShop | 阻塞，未验收 |
| ANDROID-02 师徒 | 登录页 | 同上；不能猜账号或伪造 Mentor/Mentee 业务包 | 阻塞，未验收 |
| ANDROID-03 关系 | 登录页 | 同上；不能猜账号或伪造 Marriage 业务包 | 阻塞，未验收 |

代码自动化测试或登录页截图仅证明代码/应用可启动，**不替代**上述三个业务的真机闭环。

## 证据文件

- `01-login.png`、`window-login.xml`：首次使用错误源码类名启动时留下的近乎空白截图与层级；不能用来证明模拟器桌面，仅保留用于解释随后通过 `resolve-activity` 修正组件名的过程。
- `02-login.png`：启动后登录页截图。
- `02-login-ui.xml`、`03-login-resume-ui.xml`、`06-after-resume-ui.xml`：启动/恢复后的最小 UI 层级摘要（根下为 `android.view.View`；敏感属性名已脱敏）。
- `07-before-home.png`：正确组件启动后、HOME 前的登录页截图。
- `08-home.png`：HOME 后近乎空白的截图，**不证明 Launcher 视觉内容**；Launcher 以 `foreground-home-flow.txt` 的 `dumpsys` 状态为准。
- `09-after-resume.png`：不 force-stop、使用同一组件恢复后的登录页截图。
- `03-login-resume.png`：较早采集的补充截图，不作为本次 HOME 结论的唯一依据。
- `foreground-home-flow.txt`：完整命令、时间戳及三次 `dumpsys` 状态；未保留原始全量日志。
- `lifecycle-filtered-20260809-0048.txt`：仅包含我方包生命周期/FATAL筛选输出，长标识已脱敏，不含第三方遥测。
- `device-apk.txt`：设备、APK 元数据、安装与启动状态命令输出。
- `launch-log.txt`：应用启动、资源同步阻塞与前后台恢复相关 logcat 摘要。
