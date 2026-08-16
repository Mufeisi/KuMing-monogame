# 脚本开发

脚本开发章节记录服务端脚本能够观察和调用的稳定行为，包括变量、触发器、人物、物品、怪物、地图、任务和界面能力。

!!! info "开发状态"
    变量系统和原生 TXT 基础设施已有实现对应的说明。其他脚本系统必须在实际实现核查后逐项录入，禁止从商业引擎说明书直接复制。

## 原生 TXT

- [原生 TXT 与控制流](txt-compatibility.md)
- [人物数值、PK 与金币命令](player-commands.md)
- [地图、怪物与宝宝命令](world-commands.md)
- [行会、任务与经济命令](social-quest-commands.md)
- [翎风商店、配方与规则名单](commerce-content.md)
- [TXT 系统入口与基础触发](txt-system-hooks.md)
- [NPC 对话、按钮与客户端](npc-dialog-ui.md)
- [高风险外部能力](high-risk-capabilities.md)
- [翎风 TXT 兼容声明](lingfeng-txt-compatibility-statement.md)

## 变量系统

- [变量系统概览](variables/overview.md)
- [作用域与生命周期](variables/scopes.md)
- [声明、初始化与热重载](variables/declarations.md)
- [命名小数变量](variables/decimal.md)
- [只读系统占位符](variables/read-only-symbols.md)
- [操作与显示命令](variables/commands.md)
- [完整使用示例](variables/examples.md)
- [错误与排查](variables/errors.md)
