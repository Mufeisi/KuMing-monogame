# CONTENT-06：真实内容测试服冒烟与阶段收口证据

- 执行日期：2026-08-13
- 事实源：[`../../requirements/LEG-06-内容生产工作台.md`](../../requirements/LEG-06-内容生产工作台.md)
- 固定基线：`main@b7b5f28`
- 语言：中文

## 真实内容工件

- 测试服根目录：`D:\ChuanQi\Crystal_monogame\Server-mono`；执行前确认 7000 端口无监听、该目录下无服务端进程。
- 真实地图：`MapInfo[1]`，文件 `Maps\0.map`；真实刷怪：`RespawnInfo[1]`，怪物索引 331。
- 真实 NPC：`NPCInfo[1]`，脚本拥有记录 `比奇省/边界村/传送吉旭0`。
- 地图刷怪草稿将刷新间隔从 5 改为 6，生成 1 条差异；NPC 草稿将名称从 `城际传送_吉旭` 改为 `城际传送_吉旭_内容冒烟`，生成 1 条差异。
- 两个草稿均通过既有作者会话校验并经现有 `Envir.SaveDB` 分别显式保存；全新 `Envir` 从隔离 SQLite 副本重载后确认两个变更均已持久化。
- 真实门禁位于 [`../../../eng/WindowsIntegration/Server.ContentAuthoringIntegration/RealContentSmokeTests.cs`](../../../eng/WindowsIntegration/Server.ContentAuthoringIntegration/RealContentSmokeTests.cs)。默认运行只校验安全开关；只有显式设置 `LYOCRYSTAL_CONTENT06_REAL_SMOKE=1`、测试服根目录、模式、全部目标实体、原始值、受控备份目录及 SQLite 三文件 SHA-256 后才执行。门禁先确认真实测试服与备份摘要完全一致，再从备份建立系统临时测试服副本；两个会话只向副本写盘一次，成功后再次确认真实源摘要未变，避免测试进程中断污染事实源。

## 测试服运行冒烟

- 当前源码的 .NET 10 WinForms 宿主在本机运行时于 `WinFormsComInterop.WinFormsComWrappers` 初始化阶段因 `TypeLoadException` 退出，发生在加载内容和监听端口之前；此运行时兼容问题不在 CONTENT-06 范围内，未通过改写启动接缝规避。
- 改用该测试服已验证的自包含 `Server.exe`，从同一测试服根目录加载上述已修改 SQLite。进程以隐藏窗口启动，约 54 秒后监听 7000；没有鼠标、键盘模拟或焦点抢占。
- 复用既有 LEG-01 Shared 协议探针完成真实握手、注册登录、建角、`StartGame`、地图/玩家信息收包、移动和退出：`map=1`、`movement=confirmed`，总耗时 1760 ms。
- 隐藏宿主没有主窗口句柄，关闭消息不可用；等待 30 秒后按已记录 PID 终止，随后确认 7000 端口无监听，才进入数据恢复。

## 备份与恢复

- 受控备份目录：`D:\ChuanQi\Crystal_monogame\Server-mono\Backups\CONTENT-06-20260813-234026`。
- 保存前逐一复制并校验 SQLite 三文件；恢复时先确认测试服停止、备份目录位于测试服 `Backups` 下，再覆盖精确目标并复核 SHA-256：

| 文件 | 字节 | 保存前及恢复后 SHA-256 |
|---|---:|---|
| `server.db` | 3,411,968 | `76AD4127CEFE97D0613538DB7FF95CEA376FD2D3EE56F4342897C24C0EA6523C` |
| `server.db-wal` | 0 | `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855` |
| `server.db-shm` | 32,768 | `FD4C9FDA9CD3F9AE7C962B0DDF37232294D55580E1AA165AA06129B8549389EB` |

- `Maps\0.map` 全程只读，保存前及恢复后 SHA-256 均为 `1944D3380D933A73F2FE06874CACDE47CBDE2C0C2656F7A22D15F80B592BBCCF`。
- 恢复后重新运行真实门禁的 `verify-restored` 模式：刷怪间隔为 5、NPC 名称为 `城际传送_吉旭`；协议探针创建的临时账号和角色也随整库恢复一并移除。

## 自动验证

- 加固前真实作者闭环 `apply`：1/1 通过并用于测试服运行冒烟，之后按受控备份恢复原库。
- 最终安全门禁 `apply`：1/1 通过；输出包含 `status=applied-isolated`、真实源与隔离副本路径、地图/刷怪/NPC 标识、修改前后值和差异计数；真实源摘要保持不变。
- 备份隔离与源库不变回归：1/1 通过；绝对路径及 `..` 越界路径拒绝回归：2/2 通过。
- 真实恢复验证 `verify-restored`：1/1 通过；输出包含 `status=restored` 及原始字段值。
- 真实测试服协议冒烟：1/1 通过；阶段为 `process, login, character, enter-game, enter-game, shutdown`。
- `Base05.Tests` Release 全量：433/433 通过。
- `Server.ContentAuthoringIntegration.Windows` Release 全量：30/30 通过；所有窗口测试均在后台运行。
- `Server.Library` Release 构建：0 警告、0 错误。
- `Server.MirForms` Release 构建：0 警告、0 错误。

## 结论与回滚

- 一张真实地图、一个真实刷怪和一个真实 NPC 已完成“编辑→校验→差异→显式保存→重载→测试服进图移动退出→恢复重载”的完整链路。
- 外部测试服已经恢复到执行前数据库字节状态，服务端已停止，7000 端口空闲；备份保留在上述受控目录，可再次人工复核。
- CONTENT-06 仅新增环境门控集成证据，不修改 Schema、协议、地图格式、脚本运行时、服务端运行时或生产内容。
