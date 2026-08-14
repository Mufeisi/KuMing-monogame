# GUI-10 PC/Android 动态状态投影证据

- 执行日期：2026-08-14
- 切片：`GUI-10`
- 分支：`codex/leg-07-gui-state-projection`
- 语言：中文

## 交付工件

1. Shared 新增 `CustomGuiClientStateSession`：以已接受文档/包为边界，深拷贝状态，严格校验窗口身份、过期时间、绑定类型、动作结果序号和连续状态修订。
2. PC `PcCustomGuiHost` 直接把八种状态投影到既有 MirControls；`PcCustomGuiRuntime` 从既有 `MirScene.ProcessPacket` 接收打开、增量、结果与关闭包。
3. Android `MobileCustomGuiHost` 与 `FairyGuiHost` 直接把同一状态投影到既有 FairyGUI 节点；列表替换显式释放旧行，窗口替换、关闭和断线释放整棵节点树。
4. 两端只接受 `CustomGuiAcceptedPackage`，拒绝包序列降级；错误状态不推进共享修订，也不动态创建未知控件。

## TDD 与失败样例

领域测试先引用不存在的状态会话和投影目标，得到编译失败；实现后覆盖八种状态、精确后继增量、窗口身份篡改、跳号、未知绑定、平台投影失败、动作结果重放和关闭身份。Windows 行为测试分别断言 PC MirControls 与移动 Host 得到相同文本、进度和按钮状态。

稳定诊断包括 `GUI10-STATE-PACKAGE/IDENTITY/EXPIRED/REVISION/BINDING/RESULT/CLOSED`。未知绑定、包不匹配、过期、跳号或结果重放均失败关闭当前操作并保留上一有效投影。

## 测试与门禁

| 门禁 | 结果 |
|---|---|
| GUI-10 状态会话定向 | 5/5 通过 |
| PC/移动 Adapter 与 PC 真实收包链定向 | 11/11 通过 |
| Base05 Release 全量 | 476/476 通过 |
| Windows Release 全量 | 120/120 通过 |
| PC Client Release 构建 | 0 错误；既有警告未由本切片扩张处理 |
| Android Release 构建 | 0 错误；既有警告未由本切片扩张处理 |
| `git diff --check` | 通过；仅 Git 换行提示 |

## 回滚与范围

回滚前先通过既有 `Activities` Kill Switch 停止新开动态窗口，待服务端关闭现有会话后回滚本切片。回滚不会改变 `GUI-07` 包号、`GUI-08` 会话或 `GUI-09` 权威事务。本切片不包含脚本 Hook、活动兑换事实、价格/奖励/次数持久化或客户端业务计算；后续分别由 `GUI-11/12` 实现。
