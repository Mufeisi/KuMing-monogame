# TXT-14 灰度候选验证证据

- 日期：2026-08-15 至 2026-08-16
- 候选版本：LFM2-2026-08-15-snapshot
- 候选根：`Configs/LingFengTxtPilot`
- 状态：生产候选通过，真实服务器灰度与 Android 模拟器 NPC 闭环完成

## 候选内容

- 1 个普通 NPC：`NPCs/TXT灰度向导`；
- 1 个登录触发：`SystemScripts/QManage` 的 `[@LOGIN]`；
- 1 个升级触发：`SystemScripts/QFunction-0` 的 `[@PLAYLEVELUP]`；
- `Setup.fragment.ini`、`snapshot.sha256` 与 `rollback.fragment.ini`。

候选片段显式开启 `CSharpScriptsFallbackToTxt=true`，使 C# 与 TXT 共存时允许候选系统页执行；回滚片段对称恢复为 `false`，同时关闭物理 TXT 来源，恢复变更前的执行回落策略。

逐文件 SHA-256 由 `LingFengPilotMigrationTests` 在测试运行时重新计算并与清单比对，清单多项或少项均失败。

## 自动化结果

- 灰度候选、系统 Hook、特殊触发与兼容目录目标集：27/27 通过；
- 翎风专项、物理来源、热重载、执行预算、变量和 Kill Switch：161/161 通过；
- Provider 发布后缓存 NPC 重新解析、热重载、灰度候选和服务端生命周期：59/59 通过；
- 真实 `Envir` 候选启用、停止、重启摘要一致与关闭回滚：1/1 通过；
- Base05 最终全量：698/698 通过，原始结果保存为 `txt14-base05-full.trx`；
- Launcher.DesignCore：11/11 通过；
- MkDocs `--strict --clean`：通过；
- PC VorticeDX11 Release：0 错误（36 个既有警告）；
- Server.MirForms Release 完整重建：0 错误（480 个既有警告）；
- Android x64 Debug：0 错误，模拟器安装、自动进服和灰度 NPC `[@MAIN]`/`[@VERIFY]` 实交互通过，证据见 `Docs/Evidence/TXT-12-20260815`。
- 脚本性能指标百分位门禁：2/2 通过；运行时现可导出最近 2,048 次调用的 P95/P99、样本数和全期最大值。
- 真实 `Envir.Main` 生命周期内加载并执行灰度 NPC 主页面 100 次：1/1 通过，百分位样本 100/100。
- `win-x64` 自包含单文件发布成功；最终 `Server.exe` 为 233,953,284 字节，SHA-256 为 `C4057F853106C2C1B80CAACACC5D761EBA0DBB3C2A5B64B1CC4DFB718FB89B76`。
- 最终单文件 `Server.exe --headless-variable-smoke` 退出码 0；覆盖变量初始化、作用域、持久化、公式、概率、旧 A 变量适配、跨对象访问和冲突拒绝。
- 最终单文件对真实 `D:\ChuanQi\Crystal_monogame\Server-mono\Envir` 执行 `--variable-preflight`，扫描 2,348 个文件，退出码 0，语料摘要为 `7E1E13532F37151BBC15E4B7B43383A8C1D95E3B0DC25944A2F19864D74ABE3D`；仅报告换行与旧 A 变量语义复核警告，无阻断错误。

## 已验证边界

- C# 优先与 TXT 优先均只发布同 Key 的一个来源；
- 严格快照不存在未知命令、缺失标签或引用；
- 登录与升级在 C# 已处理时不重复执行；
- `@KILLMON` 与怪物自身旧 `[@_DIE(index)]` 在同一真实死亡链中各执行一次，互不替代；
- Provider 发布后已缓存的 NPC 会在主线程重新解析；发布异常会恢复上一 Provider 与变量声明；
- 回滚片段关闭物理 TXT、恢复 `CSharpFirst` 并保持高风险能力关闭。
- 目标服部署前已备份 6,227 个文件、491,988,717 字节；360 秒保存周期、优雅退出和冷启动复验通过。
- 世界 NPC 使用唯一 `npc_id=496` 定位记录，回滚 SQL 同时限定 `npc_id` 与 `file_name`，不影响其他 NPC。
- 运行时用量记录灰度 NPC 总分派 3 次；性能记录 `[@MAIN]` 2 个样本、`[@VERIFY]` 1 个样本。

## 真实灰度结果

用户授权后，候选已部署至 `D:\ChuanQi\Crystal_monogame\Server-mono`。服务端真实数据库保存、重启与脚本热恢复均通过，脚本错误和未知命令为 0。Android 模拟器测试没有修改金币、物品或其他经济资产；页面文本明确完成地图、怪物计数与会话变量闭环。完整部署、临时热更新恢复和回滚记录见 `gray-deployment.md`。

最终工作树对应的 Base05 698/698、发布物变量烟测、真实语料只读预检与 MkDocs 严格构建均已再次通过。
