# MICRO-UX-05 测试资源发布与跨端冒烟证据

- 状态：已完成
- 执行日期：2026-08-13
- 事实源：`LEG-05-微端与发行体作者体验.md`、代码、自动化测试输出

## 可运行工件

作者工具“交付 → 发行体概览”新增“生成测试资源发布”，使用项目当前 DPAPI 私钥生成 `core-startup.zip`、`fui-retro.zip`，并输出内容完全一致的 PC `bootstrap-package-index.json` 与 Android `bootstrap-package-index.signed.json`。生成后使用项目公钥立即自检签名；私钥不写入发布目录、日志或项目 JSON。

## 跨端结果

- PC：仅配置一次主/备用微端入口；主源返回 HTTP 503 后，正式预登录更新模块自动选择备用源，完成签名验证、ZIP SHA-256 校验和事务安装；第二次运行没有新增 ZIP 请求。
- Mono/Android：仅配置一次主/备用微端入口；主源返回 HTTP 503 后，正式下载器自动选择并持久化备用仓库，事务安装模块安装同一签名资源版本；第二次规划为空且没有新增 ZIP 请求。
- 安全边界：只有索引不可获取时才尝试下一个入口；签名、防降级或客户端兼容性校验失败直接阻断，不允许通过备用入口绕过。
- 两端共用同一 `ResourceVersion`、`KeyId` 和签名清单；项目公钥仅经测试友元的异步调用域注入，生产调用继续使用编译内置信任表。
- 冒烟过程中发现并修复 Mono 下载器在 `.part` 文件流释放前执行原子改名的问题；回归测试覆盖首次下载。

## 验证

```text
dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj -p:EnableAndroidTarget=false --no-restore -v:minimal
结果：104 通过，0 失败，0 跳过；结果文件 `eng/WindowsIntegration/Launcher.PlayerShellIntegration/TestResults/leg05-gate-closeout.trx`（本地过程工件，按仓库规则忽略）。
```

既有 `Client_VorticeDX11` 项目仍报告历史 `WindowsBase` 版本冲突警告，本任务未新增构建错误。

## 回滚

回滚 LEG-05 对应独立提交即可恢复原行为；未修改项目 JSON、协议、数据库、生产可信公钥表或微端部署方式。备用入口仅扩展 Mono 客户端本地配置项，旧配置缺少该字段时保持单入口行为。
