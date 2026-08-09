# SEC-02 C5 服务端 TLS 配置界面证据

## 范围

- 在既有服务端 `ConfigForm` 网络页配置 TLS V2，不新增配置系统。
- 复用 `Settings` 与 `TlsTransportPolicy`；证书密码不进入 UI 或 INI。
- 不包含证书固定、完整客户端 TLS 宿主、SEC-03～06。

## 退出工件

- TLS 策略专项：18/18 通过；使用有效自签名 PFX，并覆盖损坏 PFX 拒绝。
- Windows STA 表单宿主：1/1 通过，实例化真实 `ConfigForm`，确认 TLS 控件加载、合法证书配置写入、无效证书路径拒绝且不修改原设置。
- Base05 全量：229/229 通过。
- `Server.MirForms` Release：构建成功，0 错误；既有警告不阻断。
- Windows CI 已加入 SEC-02 表单宿主项目的还原与测试步骤。
