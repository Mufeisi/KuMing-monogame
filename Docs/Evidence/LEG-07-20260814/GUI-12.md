# GUI-12 活动兑换双端真实闭环证据

- 执行日期：2026-08-14
- 切片：`GUI-12`
- 分支：`codex/leg-07-gui-activity-exchange`
- 语言：中文

## 交付工件

1. `CustomGuiActivityExchangeTemplate` 提供固定 `activity.exchange@1` 文档；标题、状态、余额、兑换列表、次数进度和按钮状态全部通过 GUI-10 有界状态投影更新。
2. PC MirControls 与 Android FairyGUI 的真实列表点击、按钮激活都经同一个 `CustomGuiClientStateSession` 生成 GUI-07 动作；请求序列只在真实发送成功后推进，失败可以重试。
3. `ActivityExchangeWindow` 复用 GUI-11 注册表、GUI-08 会话和 GUI-09 权威事务：1000 金币兑换 10 信用点，每个角色限一次；持久事实是既有账户金币/信用点和角色 `Flags[1998]`，没有 Schema 变更或第二事实源。
4. 合法事务依次发布状态增量和动作结果，并复用既有 `LoseGold/GainedCredit` 通知刷新经典 HUD；状态发送异常时金币、信用点和领取位精确恢复，草稿式重试语义不污染持久事实。
5. `@活动兑换` 与 `@GUIEXCHANGE` 进入既有脚本旁路入口；`Activities` Kill Switch、到期、断线和包版本切换继续由既有会话门卫关闭窗口。

## 真实链路与安全失败样例

领域纵向测试把同一玩家事实依次送入脚本注册表、窗口计划、真实协议包编码、会话门卫、权威动作事务、状态增量和动作结果，确认兑换后金币 `1500→500`、信用点 `7→17`、领取位持久化且按钮禁用。随后以相同请求重放得到 `GUI08-SESSION-REPLAY`，伪造选择得到 `GUI09-AUTH-SELECTION`；余额不足不会进入事务，状态发布失败恢复三项事实。

Windows 集成分别通过 PC MirControl 的真实点击事件和移动 Adapter 的交互节点完成“选择→提交→增量→结果→关闭”，并断言两端发送相同字段、消费同一作者工具发布的签名字节。GUI-06 已保存同一签名加载器和同一双端 Adapter 的 PC/Android 真实帧，本切片复用该渲染证据，不使用可见桌面自动化重复造截图链路：[`GUI-06-PC-1280x720.png`](GUI-06-PC-1280x720.png)、[`GUI-06-Android-1280x720.png`](GUI-06-Android-1280x720.png)。

收口审计确认两端 `RegisterAcceptedPackage` 当前只有测试显式调用，正常 PC 游戏子进程与 Android 重启尚不能从已接受 Bootstrap 状态恢复动态 GUI 包；因此本证据不把自动化注入冒充测试服完成。该跨启动器/包缓存/移动恢复的产品门禁升级为独立 `GUI-13`，在其通过前 `GATE-GUI-DYNAMIC` 与 LEG-07 保持未关闭。

## 发布与运维

作者工具发布后，将测试服 `Setup.ini` 的 `[CSharpScripts] CustomGuiPackageSequence` 设置为签名索引中的序列，再热重载或重启脚本注册表；客户端只接受 GUI-05/06 信任链通过的同序列包。回滚时先关闭 `Activities`，确认现有会话关闭，再把客户端签名资源版本切回上一已接受版本并回滚本提交；账户与角色仍由原保存域持久化。

## 测试与门禁

| 门禁 | 结果 |
|---|---|
| GUI-12 领域定向 | 21/21 通过 |
| GUI-12 Windows 定向 | 3/3 通过 |
| Base05 全量 | 490/490 通过 |
| Launcher/PC/移动 Windows Release 全量 | 123/123 通过；真实 PC 截图子进程按契约退出 |
| Server、PC、Android Shared Release 构建 | 均为 0 错误；既有 Server 可空性/弃用和 PC 依赖版本警告未由本切片扩张处理 |
| `git diff --check` | 通过；仅 Git 换行提示 |

## 范围与每日工件检查

本切片只交付一个活动兑换样板及其真实双端/服务端纵向闭环，不扩张成商城、活动平台或通用业务表达式；代码、协议行为测试、运行构建和已有双端真实帧均为可观察工件，工件数量高于过程文档数量。交流、状态、证据和提交信息均使用中文。
