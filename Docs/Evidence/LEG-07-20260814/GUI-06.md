# GUI-06 静态双端显示与签名发布证据

- 执行日期：2026-08-14
- 切片：`GUI-06`
- 分支：`codex/leg-07-gui-static-gate`
- 语言：中文

## 交付工件

1. `CustomGuiStaticPackagePublisher` 将运行描述和逻辑资源绑定写入固定入口的不可变 ZIP；`TestResourceReleasePublisher` 将其与 PC/Android 共用的三个资源包写进同一 Bootstrap 签名索引。
2. `CustomGuiSignedReleaseLoader` 复用既有 Bootstrap 签名、摘要和资源清单读取接缝，只接受签名索引拥有的核心资源包与 `custom-gui.zip`，并对内嵌清单执行 2 MiB 解压后硬上限。
3. PC 静态冒烟通过真实 `Client.exe`、`CMain`、DX 回退缓冲和 MirControls 取得 1280×720 帧；Android 通过无窗口 API 36.1 x64 模拟器、真实 `MainActivity/CMain/FairyGuiHost` 取得移动帧。
4. 图片资源未解析时由双端 Adapter 显示有界占位和替代文本，不以空白或异常代替内容；Android 外部密钥探针仅存在于 Debug 构建。

## 测试与门禁

| 门禁 | 结果 |
|---|---|
| GUI-06 定向 Windows 集成 | 4/4 通过：双端同签名发布、失败保留旧版本、真实 PC 帧、Android 下载恢复 |
| Base05 Release 全量 | 448/448 通过 |
| Windows Release 全量 | 117/117 通过 |
| PC 客户端 Release 构建 | 0 错误；既有依赖版本警告未由本切片扩张处理 |
| 作者工具 Release 构建 | 0 错误、0 警告 |
| Android Shared Release 构建 | 0 错误；既有 FairyGUI/可空性警告未由本切片扩张处理 |
| Android 完整应用 `android-arm64` Release 构建 | 0 错误 |
| Android 无窗口真实渲染 | 日志 `GUI06_ANDROID_RENDER:PASS`，图片 2048×960 |

## 可观察工件

1. 作者工具工作区：[`GUI-06-Designer-1280x800.png`](GUI-06-Designer-1280x800.png)。
2. PC 真实 DX 帧：[`GUI-06-PC-1280x720.png`](GUI-06-PC-1280x720.png)。
3. Android 真实 FairyGUI 帧：[`GUI-06-Android-1280x720.png`](GUI-06-Android-1280x720.png)；文件名沿用门禁命名，真实模拟器帧尺寸为 2048×960。

三端工件均显示“新玩家活动”标题、活动横幅/图片占位、七日登录礼、物品格、`3/7` 进度、输入和领取按钮。PC 与 Android 消费同一签名索引和同一 `custom-gui.zip`，不是分别维护的演示文档。

## 失败关闭与回滚

发布器先在同级临时目录完成资源包、GUI 包、签名索引和自检，再原子移动最终目录。测试删除必需资源后触发发布失败，断言失败目标目录不存在，并重新以生产加载器验签、验摘要和读取上一已接受版本成功。客户端遇到非法签名、摘要、资源绑定或超限输入继续沿用 GUI-05 的稳定诊断失败关闭。

`GUI-06` 不含动态协议、服务端会话或玩家状态修改。回滚本切片代码并将资源版本切回上一已接受签名索引即可；`GUI-01..05` 的 Schema、作者工具和静态 Adapter 保持可独立使用。
