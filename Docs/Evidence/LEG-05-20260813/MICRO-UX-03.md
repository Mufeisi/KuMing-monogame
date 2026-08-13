# MICRO-UX-03 入口连通性与远端身份预检证据

- 日期：2026-08-13
- 分支：`codex/leg-05-endpoint-preflight`
- 范围：主/备用入口连通性、资源版本与签名身份一致性，以及发布前失败关闭
- 语言：中文，代码标识符、命令和原始网络错误除外

## 用户可见工件

“交付 → 发行体概览”新增“检查主/备用入口”。检查覆盖项目默认入口和所有已启用区服覆盖，并逐项显示作用域、主/备用角色、地址、结果及可诊断原因。窗口模式截图位于本地 `artifacts/leg05-endpoint-preflight-20260813-1/发行体概览.png`。

“发布新版本”在写入不可变发布前强制执行同一预检；任一入口不可达、超时、响应格式无效，或远端资源版本/签名身份与项目默认值不一致时，发布失败并精确列出入口。

## 实现边界

- 复用现有微端只读 `/api/version` 契约，不新增或修改协议。
- 所有入口并行探测，单入口默认最多 3 秒；禁用系统代理和 HTTP 自动重定向；响应上限 16 KB。
- 预检不发送访问密码、不下载资源、不启动或部署 `MicroGateway`，也不写项目和资源目录。
- 用户关闭页面时取消仍在执行的入口检查。

## 验证结果

```text
真实 MicroGateway 版本身份读取：通过
主入口成功、备用入口身份不一致：通过
超时、不可达、无效响应分类：通过
入口预检定向测试：通过 3，失败 0
Launcher.PlayerShellIntegration.Windows 全量：通过 101，失败 0
窗口模式 --editor-ui-smoke：退出码 0
```

完整 Launcher 集成回归：`eng/WindowsIntegration/Launcher.PlayerShellIntegration/TestResults/leg05-endpoint-preflight-final.trx`。
