# P1-VERIFY-A 模拟器验收证据（ANDROID-01～03）

## 范围与结论

- 任务：ANDROID-01 商城、ANDROID-02 师徒（Mentor/Mentee）、ANDROID-03 关系（Marriage 系）。
- 验收设备：`127.0.0.1:21503`，Android 模拟器/虚拟设备；**不是实体真机**。
- 本次仅做模拟器可达的基本启动验收，不把模拟器结果冒充真机门禁。
- 基本启动、登录页显示、前后台恢复：通过（见 `02-login.png`、`03-login-resume.png`）。
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

- 解析后的 Android 启动组件为 `com.lommir.client.monogame.android/crc648e1c27e8d74093fe.MainActivity`。
- 首次使用源码类名直接启动会得到 `Error type 3`（类名不是打包后的组件名）；改用 `resolve-activity` 返回的组件后启动成功。
- 前台启动后 `mResumedActivity` 为上述 MainActivity；执行 HOME 后回到模拟器桌面；再次启动恢复到上述 MainActivity。
- 启动日志显示 MonoGame 图形设备初始化、FairyGUI Stage 初始化和登录界面绘制；没有 `FATAL EXCEPTION` 或 `AndroidRuntime` 崩溃。
- `PreLoginUpdate` 明确报告：`http://127.0.0.1:7777/api/file/Packages/bootstrap-package-index.json` 连接失败，且默认 FGUI 包资源探针未就绪。这是外部资源/测试服缺失的可复现阻塞，不在本任务内补服务或改配置。

## ANDROID-01～03 精确阻塞

| 功能 | 当前可达位置 | 阻塞原因 | 结论 |
|---|---|---|---|
| ANDROID-01 商城 | 登录页 | 没有可用测试服/Bootstrap 资源，且没有授权测试账号与角色，无法进入 GameShop | 阻塞，未验收 |
| ANDROID-02 师徒 | 登录页 | 同上；不能猜账号或伪造 Mentor/Mentee 业务包 | 阻塞，未验收 |
| ANDROID-03 关系 | 登录页 | 同上；不能猜账号或伪造 Marriage 业务包 | 阻塞，未验收 |

代码自动化测试或登录页截图仅证明代码/应用可启动，**不替代**上述三个业务的真机闭环。

## 证据文件

- `01-login.png`、`window-login.xml`：首次尝试使用源码类名启动时的模拟器桌面/层级；随后通过 `resolve-activity` 得到正确打包组件并成功启动，保留用于解释启动组件解析过程。
- `02-login.png`：启动后登录页截图。
- `02-login-ui.xml`：启动后 Android UI 层级（游戏画布为单一 SurfaceView，业务控件由 MonoGame 绘制）。
- `03-login-resume.png`：HOME 后重新拉起的登录页截图。
- `03-login-resume-ui.xml`：恢复后的 UI 层级。
- `device-apk.txt`：设备、APK 元数据、安装与启动状态命令输出。
- `launch-log.txt`：应用启动、资源同步阻塞与前后台恢复相关 logcat 摘要。
