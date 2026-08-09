# DB-06 交付声明

- 交付：MySQL 切换四维持续门槛、未触发维持 SQLite 的可执行判定、绑定实际源库与跨卷/UNC 副本的 DPAPI 授权记录、正式 MySQL provider 选择失败关闭门禁。
- 复用：PERF-00 指标口径、DB-03 `SqliteBackupService` 与 `integrity_check`。
- 不做：实际 MySQL 数据迁移、双写、Schema 映射、回切工具、DB-04 恢复闭环、DB-05 故障注入。
- 完成定义：阈值下沿/持续窗口、未触发拒绝迁移、真实 SQLite 本地/异地备份与缺异地失败均有测试工件；全量测试和两项 Release 构建通过。
- 语言：文档、状态与提交信息使用中文，代码标识符保留英文。
