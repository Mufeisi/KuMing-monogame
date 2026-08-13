# LEG-05 微端与发行体作者体验证据

- 日期：2026-08-13
- 收口分支：`codex/leg-05-gate-closeout`
- 已完成切片：`MICRO-UX-01`、`MICRO-UX-02`、`MICRO-UX-03`、`MICRO-UX-04`、`MICRO-UX-05`、`MICRO-UX-06`
- 语言：中文，代码标识符、命令和原始测试字段除外

## 用户可见工件

交付模式新增“发行体概览”首屏，同页显示交付方式、三项玩家登录核心、资源包文件数与完整体积、资源版本、签名身份、默认主/备用入口及区服覆盖。资源扫描在后台执行；缺失核心、重复核心或覆盖身份不一致时给出明确问题和定位按钮。

窗口模式截图保存在本地 `artifacts/leg05-distribution-overview-20260813-1/发行体概览.png`。截图验证 1280×800 下信息与修复按钮完整可见，没有新增永久侧栏或压缩设计画布。

## 自动化验证

```text
发行体概览定向测试：通过 2，失败 0
Launcher.PlayerShellIntegration.Windows 全量：通过 104，失败 0，跳过 0
窗口模式 --editor-ui-smoke：退出码 0
git diff --check：通过
```

最终完整集成结果：`eng/WindowsIntegration/Launcher.PlayerShellIntegration/TestResults/leg05-gate-closeout.trx`（本地过程工件，按仓库规则忽略）。

只读边界由自动化测试保护：概览扫描前后文件列表完全一致，并断言未生成或复制任何 `MicroGateway` 工件。入口预检证据见 [`MICRO-UX-03.md`](MICRO-UX-03.md)，PC/Android 测试资源发布、首次获取、缓存命中和自动备用源恢复证据见 [`MICRO-UX-05.md`](MICRO-UX-05.md)。事实源 [`../../requirements/LEG-05-微端与发行体作者体验.md`](../../requirements/LEG-05-微端与发行体作者体验.md) 已按门禁结论关闭。
