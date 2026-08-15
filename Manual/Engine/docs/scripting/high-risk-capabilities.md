# 高风险外部能力

- 兼容等级：C（`OPENBROWSER` 安全替代）
- 默认状态：全部关闭
- 最后复核：LFM2-2026-08-15-snapshot

## 浏览器 URL

必须同时满足以下条件才允许发送现有 `OpenBrowser` 客户端包：

```ini
[TxtScripts]
TxtScriptsHighRiskCapabilitiesEnabled=true
TxtScriptsAllowedHttpsHosts=docs.example.com;support.example.com
```

- `HighRiskOperations` Kill Switch 处于开启状态；
- URL 是绝对 HTTPS 地址，端口为默认端口或 443；
- 不含用户名/密码；
- 主机与白名单精确相等，不自动允许子域；
- URL 最长 2,048 个字符。

任一条件不满足时，严格候选快照以 `TXT-SNAPSHOT-017` 拒绝；运行时会再次检查总开关、白名单和 Kill Switch，避免配置发布后紧急关闭被绕过。日志不记录完整 URL 查询参数，防止令牌或个人信息泄漏。

这是 C 级安全替代，不兼容翎风允许任意网站的原始语义。生产建议继续保持关闭，只对白名单内的项目帮助站点按需开放。

## 文件、HTTP、JSON、数据库与管理

- 物理 TXT 只读来源已限制受控根、白名单目录、允许扩展名、文件大小、编码、重解析点和路径逃逸。
- 不向 TXT 开放通用 HTTP 请求、任意 JSON 拉取、任意 SQL 或任意进程执行。
- 名单、变量和领域数据使用既有受控 Registry/Store；数据库 Schema 仍只走 `SchemaMigration`。
- 需要管理权限的现有服务器操作不得因 TXT 兼容版本绕过权限检查和审计。

清单中的任意绝对路径写入、任意 SQL、任意 URL 和外部进程类原始能力保持 X 或 E。未知动作在严格模式由 `TXT-SNAPSHOT-014` 阻断。

## 应急关闭

先关闭 `HighRiskOperations` Kill Switch，可立即阻断已发布脚本的下一次运行；然后把 `TxtScriptsHighRiskCapabilitiesEnabled=false` 写回候选配置并预检发布。Kill Switch 的变更原因、主体、代次和时间进入现有原子审计记录。
