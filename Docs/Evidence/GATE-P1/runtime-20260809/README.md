# GATE-P1 本地服务与逍遥设备运行证据（2026-08-09）

## 结论

- 已用仓库 Android 客户端、`D:/ChuanQi/Crystal_monogame/Server-mono` 服务端和逍遥模拟器完成自动注册测试账号、自动创建角色、登录、进入游戏及 700×700 地图加载。
- 按产品决定，逍遥模拟器作为本项目 Android 实机等效设备；本记录中完整通过的步骤可标注“实机通过”。
- 商城已实机通过入口、窗口创建和复古界面 15 个固定商品格绑定；购买请求→服务端响应→状态→UI 尚未完成，因此不宣称商城完整闭环通过。
- 坐骑窗口候选误命中、活动/赏金的任务窗口构造异常是当前真实缺口；不再把测试服、资源、账号或独立实体手机列为阻塞。

## 环境与固定点

- 分支：`codex/p1-runtime-20260809`
- 基线：`ecac703`
- 阶段提交：`588ff7f`（微端认证与商店入口）、`709b92d`（复古商店固定商品格）
- Windows：Windows 10 专业工作站版 10.0.19045
- .NET SDK：10.0.200
- 设备：逍遥模拟器 `127.0.0.1:21503`，厂商/型号 `Asus / ASUS_AI2401_A`，Android 9 / API 28，分辨率 1600×900
- 测试账号：`codexp10809`；角色：`P1Hero809`。密码仅通过测试进程环境注入，未写入本文或仓库。

## 服务与资源

- 服务端：`D:/ChuanQi/Crystal_monogame/Server-mono/Server.exe`，运行时工作目录为其所在目录。
- 游戏 TCP：`0.0.0.0:7000`。
- 微端 HTTP：通配前缀 `http://+:7777/` 因 Windows URLACL 拒绝后，程序按既有回退逻辑监听 `127.0.0.1:7777`。这不是设备阻塞，逍遥通过 `adb reverse` 访问。
- 设备反向映射：`tcp:7000 -> tcp:7000`、`tcp:7777 -> tcp:7777`。
- 服务端 `MicroResources/Packages` 共 523 个文件；`bootstrap-package-index.json` SHA-256 为 `09D28504ACC896CC32004C62DF49843E1D67A97D83E985289AD1A8331CB6E6A8`。
- 更新前资源已可恢复地备份到 `D:/ChuanQi/Crystal_monogame/Server-mono/Back Up/MicroPackages/Packages-20260809-before-p1-runtime`。
- 本轮使用仓库已有 `Build/Mobile/BootstrapRepo/Packages` 部署。因隔离工作树缺少 `data-mmap` 外部源，未宣称本轮重新导出成功。

## APK 与命令结果

APK：

`Client_MonoGame.Android/bin/Debug/net10.0-android/android-x64/com.lommir.client.monogame.android-Signed.apk`

- 大小：109,457,915 字节
- SHA-256：`87A41811048AEB63A7727BBC6C01E48E13CE929131F01029A6D0F6EF8DB25935`

构建命令：

```powershell
dotnet build Client_MonoGame.Android\Client_MonoGame.Android.csproj -f net10.0-android -c Debug -r android-x64 -p:MobileBootstrapAssetMode=Micro -p:AndroidPackageFormat=apk -v:minimal
```

结果：退出码 0，用时 160.9 秒；既有可空性、XML 注释和包漏洞警告未升级为本任务错误。

安装和网络命令：

```powershell
adb -s 127.0.0.1:21503 install -r -d <apk>
adb -s 127.0.0.1:21503 reverse tcp:7000 tcp:7000
adb -s 127.0.0.1:21503 reverse tcp:7777 tcp:7777
```

结果：覆盖安装 `Success`；两条映射均由 `adb reverse --list` 返回。

## 自动化与运行验证

| 验证 | 结果 | 证据 |
|---|---:|---|
| `MobileMountHudRegressionTests` | 6/6 | 商店入口安全区布局及与其他入口不重叠 |
| `GameShopStateTests` | 6/6 | 商店状态专项编译与回归通过 |
| Base05 全量 | 198/198 | 退出码 0，用时 99.8 秒；0 失败、0 跳过 |
| 自动注册/登录/建角 | 通过 | 账号 `codexp10809`、角色 `P1Hero809`；日志不记录密码正文 |
| 微端资源 | 通过 | `data-mmap`、`maps-numeric`、`lib-tiles` 等包下载并应用 |
| 进图 | 通过 | `0.map`，700×700，Objects=18，MiniMap=1/1，后台加载完成 |
| 商店入口与窗口 | 通过 | `FairyGUI Action: Shop`；创建 `UI/商店_DShopUI` |
| 商店固定商品格 | 通过 | 2026-08-09 09:46:27 记录 `商店窗口固定商品格绑定完成：Cells=15`；本轮之后无新的“未找到商品列表”错误 |
| 商店购买闭环 | 未验收 | 尚无购买请求、响应、状态和 UI 联合证据 |
| 师徒/关系/封印租赁/钓鱼 | 部分通过 | 入口动作和窗口创建已触发，业务请求响应闭环待逐项验证 |
| 坐骑 | 未通过 | 组件候选误命中 `UIRes/RankingGrideItem`，需修正窗口解析 |
| 活动/赏金 | 未通过 | `Quest` 窗口 `ConstructFromResource` 出现 `NullReferenceException`，需修正 |

## 证据文件

- `login.png`：逍遥登录界面。
- `register.png`：逍遥注册输入流程。
- `smoke-result.png`：自动建角进入游戏后的 HUD、地图资源下载和业务入口。
- `MobileRuntime-after-fixed-shop.log`：完整运行日志；关键行包含登录、进图、商店窗口和 15 格绑定。
- `MobileErrors-after-fixed-shop.log`：错误日志；09:46 后无商店列表错误，同时保留腰带栏及已知 UI 缺口，避免筛掉失败证据。

逍遥在地图资源加载完成后受 OpenGL 硬件表面影响，`adb screencap` 可能返回纯黑画面；因此商店固定格以运行日志为主证据，不把黑图当作功能失败，也不把它当作可视通过截图。
