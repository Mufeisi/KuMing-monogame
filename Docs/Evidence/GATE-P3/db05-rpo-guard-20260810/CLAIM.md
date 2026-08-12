# DB-05 任务声明

- 基线：`cef790f`
- 目标：生产 `SaveDelay` 强制 1～5 分钟，并以真实进程故障注入验证最坏调度与提交窗口。
- 做：共享 RPO 策略、Settings 载入、ConfigForm 保存、正式/测试服启动口径、Envir 调度、SQLite 提交、强停与重载 T-10 证据。
- 不做：改写保存线程或事务、DB-04 恢复、DB-06 MySQL 门槛。
- 方法约束：复用既有 `ProductionSecurityPolicy`、Envir、SqlServerPersistence 和 Base05 测试宿主；仅 internal 测试时钟，不新增产品工具；存储提交失败不伪报为可保证 RPO。
- 完成定义：1/5 通过，负数/0/>5 正式失败关闭；真实自动保存提交后强停并重载，窗口小于 5 分钟；专项、全量和构建通过。
- 语言：交流、文档、提交信息全部中文。
