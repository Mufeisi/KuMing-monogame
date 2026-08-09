# DB-05 RPO 配置强校验

## 正式门禁

`Setup.ini` 的 `[Database] SaveDelay` 单位为分钟。所有环境拒绝零和负数；`TestServer=False` 的正式环境只接受 1～5 分钟，测试服允许大于 5 分钟。

`ProductionRpoPolicy.ValidateConfiguredSaveDelay` 根据同一个 `Settings.TestServer` 决定是否执行正式上限，并接入：

- `Settings.Load`：配置载入时立即验证。
- `ConfigForm.TrySave`：写入任何设置前验证候选值。
- `ProductionSecurityPolicy.ValidateAndApply`：宿主创建工作线程前再次失败关闭。

因此测试服在配置载入和正式宿主启动前采用同一放宽口径，正式服也不能靠绕过界面或程序内赋值逃过 1～5 分钟门禁。

## 自动保存与故障注入

Envir 首次保存截止和每次重新排期均调用 `GetNextAutoSaveDeadline`。生产默认时间仍来自现有 `Stopwatch`；`EnvirStartOptions.ElapsedMillisecondsProvider` 是 internal 测试接缝，只用于在自动化中确定性推进真实主循环，不向正式配置暴露。

T-10 不再只验证算术函数。Base05 的同一测试程序集以子进程模式执行以下闭环：

1. 启动现有 `Envir`，注入真实 `SqlServerPersistence` SQLite 实例，设置正式最坏值 5 分钟。
2. 将测试时钟推进到 300000ms，让现有主循环触发四域自动保存；等待 Accounts 的真实单写线程事务产生成功提交代次并排空。
3. 把在线账号金币从已提交的 100 改为未提交的 777，再推进到下一截止 600000ms 前 1ms；重新读取数据库确认此时仍为 100。
4. 父测试取得标记后用 `Process.Kill(entireProcessTree: true)` 强制终止 `dotnet test` 子进程树，不调用 `Envir.Stop` 或最终保存。
5. 用新 `SqlServerPersistence` 实例模拟重启，确认账号金币仍为最后成功提交值 100；可复算未提交窗口为 299999ms，小于 5 分钟。

测试同时记录故障进程 PID、非零退出码、最后成功提交代次、逻辑提交/崩溃时刻和重启读取值到 TRX 输出。它真实覆盖 Envir 调度、快照交接、SQLite 后台提交、进程强停和重载边界；仅用内部时钟压缩等待，不新造产品宿主或迁移工具。

该结论针对服务器进程/主机在正常持久化能力下突然终止。磁盘、权限或 SQLite 事务失败时不能承诺继续满足时间 RPO；既有保存韧性策略会记录失败并执行关服保护，这类情况必须作为数据安全事故处理。

## 阶段边界

DB-05 不改变 DB-01/02 的快照、单写线程、事务或失败策略，不实现 DB-04 恢复，也不改变 DB-06 MySQL 门槛。DB-01～06 均通过后，GATE-P3 才关闭。
