# 行会、任务与经济命令

- 兼容等级：B
- 适用入口：NPC 的 `#IF` 与 `#ACT`
- 最后复核：LFM2-2026-08-15-snapshot

## 任务状态

```text
#IF
ISQUESTACTIVE 10
ISQUESTCOMPLETED 11
```

两个别名只在显式 `LFM2-` 兼容版本启用，分别映射到现有 `CHECKQUEST 任务编号 ACTIVE` 与 `CHECKQUEST 任务编号 COMPLETE`。任务编号必须是大于零的整数；非法编号会在候选快照发布前以 `TXT-SNAPSHOT-015` 拒绝，不会等玩家执行时静默忽略。

## 行会成员关系

```text
#IF
INGUILD 沙巴克
#ACT
ADDTOGUILD 沙巴克
TRYREMOVEFROMGUILD
```

`INGUILD` 不带名称时检测玩家是否属于任意行会，带名称时精确检测。`ADDTOGUILD` 不直接篡改成员集合：只有未入会玩家和已存在行会才进入项目现有邀请确认流程。`TRYREMOVEFROMGUILD` 复用现有退出行会路径，未入会或缺少成员等级时无副作用，并清理新人/行会特效。

## 行会经验事务

```text
#ACT
GIVEGUILDEXP 100
```

提交前同时验证玩家已入会、数量是大于零的 `uint`。任一条件失败都不修改行会经验；快照中的零、负数、非整数或多余参数会被严格预检拒绝。

金币、物品奖励与回收继续使用[人物、物品与金币命令](player-commands.md)中的事务规则。本页不把邮件、摆摊、攻城或客户端商店窗口冒充为已兼容命令；它们按各自协议、权限与 UI 门禁单独推进。

## 线程与排错

- 任务状态读取和行会状态修改均沿用 NPC 主线程执行路径。
- 行会不存在、玩家已经入会或玩家不在行会时，不会写入半成品成员关系。
- 严格预检错误包含源文件、行号和 `TXT-SNAPSHOT-015`。
