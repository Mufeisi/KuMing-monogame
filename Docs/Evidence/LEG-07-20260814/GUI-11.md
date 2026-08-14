# GUI-11 脚本旁路 Hook 证据

- 执行日期：2026-08-14
- 切片：`GUI-11`
- 分支：`codex/leg-07-gui-script-hook`
- 语言：中文

## 交付工件

1. 既有 `ScriptRegistry` 新增 `RegisterCustomGuiWindow`，脚本只能登记有界窗口身份、状态提供器和 GUI-09 动作规则；没有新增脚本编译器、运行时、反射入口或客户端权威字段。
2. `CustomGuiScriptRegistry` 限制窗口数、单窗口动作数和最长有效期；状态先深拷贝，再通过真实 `CustomGuiOpen` 协议编码器复用 GUI-07 的条目、文本、列表、物品槽和载荷上限。
3. `ScriptApi.OpenCustomGui` 捕获当前原子注册表，并在游戏主线程读取玩家状态；真实 `MirConnection` 继续依次使用 GUI-08 会话门卫和 GUI-09 文档动作快照，不存在绕过入口。
4. 热重载在主线程关闭受影响脚本窗口、移除旧动作委托后才发布新注册表；同版本重新编译可替换 Hook，包序列或文档修订降级失败关闭且保留当前快照。
5. 继续复用持久化 `Activities` Kill Switch；停用时 GUI-08 阻止新开并关闭现有动态窗口，没有建立第二套运维开关。

## TDD 与失败样例

测试先引用尚不存在的脚本窗口注册表、计划和批量规则接口并得到编译失败；实现后覆盖真实热更编译、状态快照、协议上限、白名单动作、同版本 Hook 替换、降级拒绝和热重载委托释放。

稳定诊断包括 `GUI11-HOOK-WINDOW/VERSION/LIFETIME/DATA/STATE/ACTIONS/PLAYER/MAINTHREAD/OPEN/RELOAD`。未知窗口、状态提供异常、重复绑定、未登记动作和版本降级均不会打开窗口或执行未知业务动作；异常文本只对外返回稳定诊断，服务端日志只记录异常类型。

## 测试与门禁

| 门禁 | 结果 |
|---|---|
| GUI-11 领域定向 | 7/7 通过 |
| Base05 Release 全量 | 483/483 通过 |
| Server Content Windows 全量 | 30/30 通过 |
| Launcher/PC/移动 Windows 全量 | 120/120 通过 |
| Server Release 构建 | 0 错误；既有警告未由本切片扩张处理 |
| `git diff --check` | 通过；仅 Git 换行提示 |

## 回滚、范围与每日工件检查

回滚前先通过既有 `Activities` Kill Switch 停止新开并关闭动态窗口，再回滚本切片；GUI-07 协议、GUI-08 会话和 GUI-09 权威事务可以独立保留。本切片不包含具体兑换表、奖励、次数持久化、客户端价格计算或第二业务场景，真实纵向闭环由 `GUI-12` 完成。

本切片形成服务端代码、领域测试、两组 Windows 回归和可运行构建，工件数量高于过程文档数量；交流、状态、证据和提交信息均使用中文。
