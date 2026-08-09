# SEC-04 管理端安全配置

现有管理 HTTP 服务继续使用 `[Network] StartHTTPService`、`HTTPIPAddress` 与 `HTTPTrustedIPAddress`，但所有非 `/api/` 微端资源请求现在还必须通过独立 Bearer 令牌鉴权。管理令牌不复用 `GMPassword`。

## 监听边界

- 回环地址允许 HTTP，例如 `http://127.0.0.1:7777/`；适合本机反向代理或本机运维程序。
- 明确的 RFC1918 IPv4、IPv6 ULA/链路本地地址只允许 HTTPS。
- 公网 IP、通配符、`0.0.0.0`、主机名和内网明文 HTTP 在启动管理服务前直接拒绝。
- `HTTPTrustedIPAddress` 继续限制唯一允许的来源 IP；来源不可识别时拒绝。

## 独立凭据与角色

服务端从 Windows DPAPI 当前用户范围读取两个独立令牌：

| 环境变量 | 角色 | 权限 |
|---|---|---|
| `administrator-token` | `Administrator` | 状态、广播、开户、名单维护 |
| `operator-token` | `Operator` | 状态、广播 |

请求使用 `Authorization: Bearer <令牌>`。两个令牌均未配置时返回 503；缺失或错误令牌返回 401；角色不足返回 403。令牌应使用密码学安全随机源生成，正式服至少 32 个字符。首次部署分别通过一次性 `LYOCRYSTAL_IMPORT_ADMIN_TOKEN`、`LYOCRYSTAL_IMPORT_OPERATOR_TOKEN` 注入并启动；进程导入后清除自己的环境副本。令牌不能写入 INI、脚本、命令历史、日志或仓库。

## 审计

每次来源拒绝、鉴权失败、越权或鉴权成功都会通过不可静默丢弃的警告级接缝写入现有服务日志，固定前缀为 `ADMIN_AUDIT`，字段包括 UTC 时间、确定性散列来源关联标识、HTTP 方法、规范化动作、角色与鉴权结果。审计不记录来源 IP 明文、Authorization、令牌、查询串或请求正文；HTTP 运行日志也只记录绝对路径。管理员与操作员令牌配置为同一值时整体按未配置处理，不允许角色提升。

## 微端边界

`/api/` 是既有微端资源接口，继续使用 `MicroAuthor/MicroCode`，不属于 SEC-04 管理 API。本任务没有修改它的协议或资源服务行为；其签名格式由 SEC-06 处理。
