# 兼容模式与迁移

- 功能状态：实验性（工具与启动门禁已实现）
- 首次支持版本：开发版 2026-08-15
- 部署状态：必须使用实际运行服脚本完成预检后才能切换兼容模式

兼容迁移采用“只读预检—人工处理—摘要确认—启动生效”的流程。预检不会修改、移动或重新编码脚本，也不会自动把有歧义的语句改成另一种含义。

## 三种模式

| 模式 | 用途 | 启动要求 |
|---|---|---|
| `LegacyCurrent` | 默认模式；继续使用当前 LyoCrystal 变量实现 | 不要求预检 |
| `Audit` | 启动时扫描实际脚本并记录风险，但不因风险阻止启动 | 配置有效脚本根目录 |
| `LingFengCompatible` | 表示管理员已经审核当前脚本快照 | 预检无阻断错误，且配置的确认摘要完全一致 |

模式在服务端启动时锁定。运行中修改配置不会改变当前进程的模式；切换或回滚模式需要重启服务端。新增、删除或修改被扫描文件会改变摘要，下一次以 `LingFengCompatible` 启动时必须重新审核并确认。

## 第一步：命令行只读预检

先备份实际运行服，再对实际 `Envir` 或脚本根目录运行：

```powershell
dotnet Server.dll --variable-preflight "D:\运行服\Mir200\Envir"
```

输出包含扫描根目录、TXT/INI 文件数量、SHA-256 内容摘要、固定前缀引用数量以及逐文件诊断。退出码含义：

| 退出码 | 含义 |
|---|---|
| `0` | 扫描完成且没有阻断错误；警告仍需人工确认 |
| `2` | 命令参数错误 |
| `3` | 根目录、内容、编码、范围或动态引用存在阻断错误 |

## 第二步：处理诊断

| 诊断代码 | 级别 | 需要确认的内容 |
|---|---|---|
| `VAR07-ROOT-001` | 错误 | 扫描根目录不存在 |
| `VAR07-CONTENT-001` | 错误 | 没有可扫描的 TXT/INI 内容 |
| `VAR07-FILE-001` | 错误 | 文件无法只读打开 |
| `VAR07-ENCODING-001` | 错误 | 文件不是有效 UTF-8 或 CP936 |
| `VAR07-RANGE-001` | 错误 | 固定编号超出该前缀允许范围 |
| `VAR07-DYNAMIC-001` | 错误 | 动态拼接变量名，静态扫描无法证明含义 |
| `VAR07-A-READ/WRITE` | 警告 | A 已是全服持久字符串，确认原脚本是否依赖旧 NPC 私有临时语义 |
| `VAR07-RESERVED-001` | 警告 | N998/N999 是保留兼容槽位 |
| `VAR07-NEWLINE-001` | 警告 | 同一文件混用 CRLF/LF |
| `VAR07-PATH-001` | 警告 | 变量存取命令包含绝对路径 |

错误必须修复或改成可审计的显式引用。警告不能机械忽略，应记录脚本位置、业务含义和处置结论。

## 第三步：先进入 Audit

在服务端配置的 `[CSharpScripts]` 节加入：

```ini
[CSharpScripts]
ScriptVariableCompatibilityMode=Audit
ScriptVariablePreflightPath=D:\运行服\Mir200\Envir
ScriptVariableCompatibilityAcknowledgement=
```

重启后检查 `[Variables][Preflight]` 日志，并对选定 NPC 完成登录、对话、跨角色读写、小退重登和服务重启冒烟。`Audit` 不会自动重写脚本，也不会把警告视为已接受。

## 第四步：摘要绑定启用

确认报告和冒烟结果后，把预检输出的完整摘要写入配置：

```ini
[CSharpScripts]
ScriptVariableCompatibilityMode=LingFengCompatible
ScriptVariablePreflightPath=D:\运行服\Mir200\Envir
ScriptVariableCompatibilityAcknowledgement=预检输出的完整SHA256摘要
```

若目录为空、存在错误或摘要不一致，服务端在启动线程建立前失败关闭。这样可防止“审核的是一套脚本，启动的却是另一套脚本”。

## 灰度与回滚

推荐按以下顺序发布：

1. 备份数据库、Legacy 变量文件和实际脚本；
2. `LegacyCurrent` 下执行一次独立 CLI 预检；
3. 切换 `Audit`，观察变量错误、保存失败和目标离线日志；
4. 处理全部阻断错误及 A 变量歧义；
5. 固定脚本版本，记录摘要并切换 `LingFengCompatible`；
6. 异常时恢复 `LegacyCurrent` 并重启，再按备份边界恢复数据或脚本。

回滚模式不会自动撤销已经成功保存的变量值。若变更包含数据语义迁移，必须同时准备对应的数据恢复方案。

!!! warning "不能用说明书目录代替实际脚本"
    预检必须指向准备启动的真实运行服内容。仓库源码、示例脚本或翎风说明书都不能证明生产脚本已经兼容。
