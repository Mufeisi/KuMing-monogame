# LFENV-14 验证证据

- 状态：已验证
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：高频比较与取反、魔法攻击/受击触发、人物死亡/击杀触发、代表 Envir 所选长尾严格预检

## 工件摘要

- `EQUAL/LARGE/SMALL` 与 `NOT/!` 进入真实 `NPCSegment.Check` 链；变量和服务器常量仍先经统一替换，非法数值失败关闭。
- `@MAGICATTACK/@MAGICSTRUCK` 复用实际技能 ID 的伤害后置快照；普通攻击不会冒充魔法事件。
- `@PLAYDIE/@KILLPLAY` 从真实人物死亡链派发，英雄及人物/英雄宝宝归属到主人；无人物归属或自杀不执行 `@KILLPLAY`。
- 死亡 QFunction 与旧 DefaultNPC `[@_Die]` 保持独立，测试同时断言 QFunction 奖励和旧延迟入口存在；C# Handler 已注册时抑制同一 QFunction TXT 触发。
- 严格预检在 `[GOODS]/[TRADE]` 等数据段边界停止解析动作；代表 Envir 的本批标准 `#IF` 命令和四个标签阻断为零。`#IF(n)/#OR`、对象前缀、客户端扩展与其余长尾仍保留严格诊断，未伪报整服 E1/E2 完成。

## 自动化验证

显式构建：

```powershell
dotnet build Tests/Base05.Tests/Base05.Tests.csproj --no-restore -p:WarningLevel=0 --verbosity:minimal
```

结果：生成成功，0 警告，0 错误。

专项覆盖兼容清单、严格预检、NPC 检测、系统触发与真实战斗/死亡入口：49/49 通过，0 失败，0 跳过。TRX：`lfenv14-targeted.trx`。

随后执行 Base05 全量回归：874/874 通过，0 失败，0 跳过，用时 2 分 35 秒。TRX：`lfenv14-full.trx`。

两份提交版 TRX 已脱敏用户名、设备名、用户目录和绝对工作区路径，统一为 UTF-8 无 BOM + CRLF；XML 重新读取后计数与测试结果不变。目录内 `.gitattributes` 固定 TRX 为 CRLF，保证干净检出后的哈希稳定。

说明书修改完成静态路径和差异检查。执行 `python -m mkdocs build --strict --config-file Manual/Engine/mkdocs.yml` 返回 `No module named mkdocs`；当前环境未安装 `mkdocs`，因此未把缺失工具伪报为站点构建通过。

## 双轴自审结论

- Spec 自审：无 BLOCKER。每个本批检测/触发均有真实可观察断言；未实现复合条件与其余长尾继续失败关闭，阶段边界与最终 E1/E2 定义一致。
- Standards 自审：无 BLOCKER。没有新增万能 Provider；事件复用既有主线程、重入、预算和不可变快照接缝；默认非翎风解析不进入新增分支；测试全局状态对称恢复。

## SHA-256

- `lfenv14-targeted.trx`：`55ECECD2556A7BC5244AD1215CC75865029CB1EC16868092B7FFB708B79C4A7C`
- `lfenv14-full.trx`：`30FBBE25B7EF0598BFC206AE64A8C818651479D0F625067446C09FF649A6CF87`

哈希对应最终实现、清单和测试工作树；后续阶段若修改检测解析、伤害/死亡派发、严格预检或兼容清单，必须重跑并刷新证据。
