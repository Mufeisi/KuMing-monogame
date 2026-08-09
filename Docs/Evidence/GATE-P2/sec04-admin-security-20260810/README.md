# SEC-04 管理端安全证据

## 交付结果

- 管理 API 在既有来源 IP 限制之外，必须使用独立 Bearer 令牌；不读取或复用游戏 `GMPassword`。
- `Administrator` 可执行全部既有管理动作，`Operator` 只允许状态与广播。
- 回环可使用 HTTP；内网地址强制 HTTPS；公网、通配地址和内网明文 HTTP 在管理服务启动前拒绝。
- `ADMIN_AUDIT` 记录来源、方法、规范化动作、角色与鉴权结果，不记录令牌、Authorization 或查询串。
- 既有 `/api/` 微端资源接口保持原鉴权与行为，不纳入管理角色。

## 验证结果

| 验证 | 结果 |
|---|---|
| `AdminSecurityTests` 专项 | 4/4 通过，见 `sec04-admin.trx` |
| Base05 全量 | 243/243 通过，见 `sec04-base05.trx` |
| `Server.Library` Release | 0 错误；2 个既有依赖漏洞警告不阻断 |

真实 `HttpListener` 用例验证缺失令牌与错误令牌返回 401、操作员读取状态成功、操作员访问开户端点返回 403、管理员通过鉴权。纯策略用例覆盖公网/通配地址、内网明文 HTTP、角色矩阵、未配置状态、固定时间令牌比较入口及无秘密审计格式。

## 命令

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~AdminSecurityTests" --logger "trx;LogFileName=sec04-admin.trx"
dotnet build Server/Server.Library.csproj -c Release --no-restore
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-build --logger "trx;LogFileName=sec04-base05.trx"
```

## 边界

环境变量令牌是 SEC-05 受保护密钥存储接入前的过渡。本工件关闭 SEC-04，但 SEC-05、SEC-06 仍未完成，GATE-P2 仍未关闭。
