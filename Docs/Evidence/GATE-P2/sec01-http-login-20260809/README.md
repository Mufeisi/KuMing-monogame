# SEC-01 HTTP 登录与客户端宿主收口证据

## 范围

- 服务端：HTTP 登录完整账户事务只在主线程执行；覆盖停服竞态、排队超时取消、已开始执行等待及异常回滚。
- PC/移动：真实 `Settings.Load/Save` 清理旧密码且不落盘；登录提交进入真实 `Network.Enqueue`。
- 不含：SEC-02 及 GATE-P2 其他安全项。

## 自动验证

- `Sec01LoginTransactionTests`：5/5 通过。
- `Sec01.ClientIntegration.Windows`：1/1 通过；真实调用两端 `Settings.Load/Save`，并从 PC 实际发送队列取回提交的登录包。
- Android Release arm64 AOT：构建成功，0 错误；输出仍含仓库既有警告。

## 逍遥宿主探针

显式 Intent extra `sec01HostProbe=true` 触发一次性真实宿主探针；默认启动不执行。探针：

1. 保存原运行时账号状态；
2. 使用本次内存假凭据调用生产 `Submit`；
3. 验证 `Network` 待发送队列增加 1；
4. 验证配置中无 `Password`、`RememberPassword` 及本次假密码；
5. 恢复原账号状态并再次保存。
6. 写入结果后立即结束并销毁探针进程，丢弃未发送的假登录包，不污染正常启动。

逍遥运行日志结果：`SEC01_HOST_PROBE:PASS`，随后确认探针进程已退出。日志不包含账号或密码正文。
