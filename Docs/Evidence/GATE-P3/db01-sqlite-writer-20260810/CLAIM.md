# DB-01 SQLite 单写线程任务占用声明

- 任务：DB-01 WAL、busy_timeout 与专用单写线程
- 会话：当前 Codex 主会话
- 分支：`codex/p3-db01-sqlite-writer-20260810`
- 工作树：`C:\Users\luo\.codex\worktrees\p3-db01-sqlite-writer-20260810`
- 基线：`99a204c`
- 状态：已完成；规格轴与规范轴复审均无阻断项
- 做：SQLite WAL 与 busy_timeout、专用单写线程、保存请求合并、停服等待最终提交、失败告警与有限重试、T-09 和证据
- 不做：DB-02 不可变快照 DTO/保存代次、DB-03 备份恢复、数据库 Schema 重造、MySQL 迁移
- 方法约束：复用现有持久化与生命周期接缝；玩家状态仍只由主线程修改；数据库表变更必须走既有 SchemaMigration
- 预估时间：2～4 小时
- 完成定义：高并发请求下同时仅一个 SQLite 保存任务；新请求合并；关服等待最后一次提交；失败策略有测试；专项、全量与构建通过并归档
- 语言：全部输出中文，代码标识符、命令和报错原文除外
