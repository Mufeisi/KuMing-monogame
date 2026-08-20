# 项目上下文

- 状态：已实施
- 最后复核：2026-08-20
- 事实源：运行行为以 `Server/Persistence`、Schema 与测试为准；架构决定见 `Docs/Architecture/ADR-0001-sqlite-three-authorities.md`。

## 持久化术语

| 术语 | 含义 |
|---|---|
| Identity | 账号身份与安全数据，不包含钱包 |
| Character Runtime | 账号钱包、角色、英雄、物品实例、公会、邮件、拍卖、NPC 商品、城战状态、备份与归档 |
| World Definition | 地图、物品模板、怪物、NPC、任务、技能、商城、龙、攻城与刷新定义 |
| Authority | 对一类数据具有唯一写入权的数据库边界 |
| Checkpoint | 单一 Authority 的一致性提交；不存在跨库 `All` |
| Generation | 成功提交或迁移产生的单调代次 |
| Activation manifest | `database-layout.json`，唯一指向已完成并可启动的三库 generation |
| Lifecycle | 角色的 `active`、`pending_deletion`、`archived` 状态 |
| Item location | `item_locations` 中物品实例的唯一运行时位置 |
| Snapshot lineage | Character 运行域行上的 `snapshot_generation`/`snapshot_active`；当前只记录来源代次，不凭快照缺失自动隐藏或删除资产 |

## 不变量

- SQLite 运行时事实源固定为 `identity.db`、`characters.db`、`world.db`。
- 空、未知或 `Legacy` Provider 必须拒绝启动。
- World 只由编辑器显式保存；自动保存和关服保存只提交 Character Runtime。
- Identity 安全变更即时提交。
- 一个物品实例只能有一个位置；跨库引用使用稳定 ID 并在应用层校验。
- detached Character snapshot 必须在修改内存前通过跨库账号、物品模板与所有权引用校验。
- `NPCScript.Goods` 仍由 C#/脚本定义，不复制为数据库事实源。
- 未完成的 activation manifest 不得启动。
