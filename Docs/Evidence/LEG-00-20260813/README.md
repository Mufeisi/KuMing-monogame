# LEG-00 可复现基线证据

- 证据日期：2026-08-13
- 源码提交：`db7290796281561f6c1eecc9a2797d00db185864`
- 任务分支：`codex/leg-00-baseline`
- 活动规格：[`../../requirements/LEG-01-真实启动与进图验证.md`](../../requirements/LEG-01-真实启动与进图验证.md)
- 证据性质：不可改写的本地执行快照；动态任务状态以活动规格为准

## 结论

LEG-00 的范围、非范围、真实环境、验收账号策略、失败判据、验证入口和回滚路径已经明确。当前代码与仓库内资源快内环通过；外部测试根、服务端、完整 PC 客户端、启动器候选制品和五类参考源码入口存在。检查时 LyoCrystal 测试服未运行，因此本证据不声称真实登录或进图已经通过；该行为属于 LEG-01。

## 实际执行结果

| 检查 | 结果 |
|---|---|
| `git status --short --branch` | 从干净 `main` 建立独立分支；无既有未提交修改 |
| `dotnet --version` | `10.0.200`，与 `global.json` 一致 |
| `ResourceBaseline.ps1 -Action Validate -Scope Repository` | 通过；3 个仓库资源项和缺失契约通过，外部资源按 Repository 范围跳过 |
| `Launcher.PlayerShellIntegration.Windows.csproj` Release 测试 | 85/85 通过，0 失败，0 跳过，耗时 15 秒 |
| 参考源码入口 | 91/JONE、BLUE_64、AppleM2、54MAX、188BLUE 五类入口均存在 |
| 运行根 | `D:/ChuanQi/Crystal_monogame` 与 `Server-mono/Server.exe` 存在 |
| PC 客户端 | `Build/Client_VorticeDX11/Client.exe` 与完整资源目录存在 |
| 当前服务 | 检查时未发现 LyoCrystal Server/Client/Launcher 进程或 7000/7100/7200/8000 监听 |

首次测试因没有还原依赖而报告缺少 `obj/project.assets.json`；执行 `dotnet restore LyoCrystal.Launcher.slnf` 后重跑通过。这是环境准备问题，不是测试失败。

## 运行制品指纹

| 制品 | 产品版本 | 字节 | SHA256 |
|---|---|---:|---|
| `D:/ChuanQi/Crystal_monogame/Server-mono/Server.exe` | `1.0.0+9af0616f6d6e31169e2614a546179d47e516b881` | 233977581 | `53a6be3484966e9c43dff4f5c5a20dd37fcd2a76386202d32d496988aff4daeb` |
| `Build/Client_VorticeDX11/Client.exe` | `1.0.0+ad21a2fef10012f8f5714738c1d1788171119fa7` | 179712 | `ef9660b2dc7f891eee67e886b588307c71691a87cb1e72ae9f2d17f392089875` |
| `artifacts/启动器配置器-本地完整客户端优先-20260812/传奇启动器配置器-本地完整客户端优先版.exe` | `1.0.0+cdc06435f0f8dd05752c397d87f49e1a8cae5e3c` | 223662202 | `e8c7ec6e7d2b00a5697ab10ecf136cdb0be13f1c81f4379aa448a37a1fc6c9c8` |

这些制品是 LEG-00 当前基线，不是正式发布来源。LEG-01 开始真实冒烟前必须重新记录实际使用制品的提交和摘要。

## 验收账号与秘密边界

历史证据表明同一外部测试根曾用专用账号 `codexp10809` 和角色 `P1Hero809` 完成登录、进图及地图加载。LEG-01 可复用该身份；凭据不可用时现场注册新的 `leg01-*` 临时账号和 `LEG01*` 角色。密码只由当前测试进程环境或交互输入提供，不写入仓库、命令行、截图或日志。不得读取密码字段、清理既有账号或覆盖运行数据库。

## 清洁室声明

参考源码只用于行为、状态、不变量和失败场景的中文规格。未复制 Delphi 实现代码、资源、皮肤、私有协议常量、旧加密或线程实现；参考源码没有加入本仓库构建与发布路径。

## 快慢验证与回滚入口

- 快内环：活动规格中的两个启动器 Windows 专项测试。
- 慢外环：阶段末 `ResourceBaseline.ps1 -Action Validate -Scope All`、四类资源场景和一次真实玩家入口冒烟。
- Evidence：LEG-01 使用新的 `Docs/Evidence/LEG-01-<日期>/`，不覆盖本目录。
- 回滚：代码回到任务起始提交；测试数据只新增不删除；资源更新前备份并按摘要恢复；不执行正式发布。
