# SEC-02 TLS 运维配置

本文是当前 TLS V2 的最小上线配置说明。正式服只开放 V2；V1 明文不得暴露公网。

## 服务端

在服务端 INI 的 `[Network]` 节配置监听地址、端口和证书路径：

```ini
IPAddress=0.0.0.0
Port=7000
TlsEnabled=true
TlsPort=7001
AllowLegacyV1=false
TlsCertificatePath=certs/server.pfx
```

PFX 私钥密码只在启动进程的受保护环境中注入为 `LYOCRYSTAL_TLS_CERT_PASSWORD`，不写入 INI、日志或仓库。证书必须包含客户端使用的 DNS/IP SAN；客户端 `TlsServerName` 必须与 SAN 匹配。协议最低 TLS 1.2（可协商 TLS 1.3）。

## PC 与 Android 客户端

客户端 INI 的 `[Network]` 节使用独立 TLS 端口和名称：

```ini
UseTlsV2=true
TlsPort=7001
TlsServerName=game.example.com
```

V2 握手失败不会降级 V1。错误提示会引导检查客户端系统时间、`TlsServerName`/证书 SAN、证书链和有效期；请先修正这些条件再重试。

## 证书轮换、回滚与监控

1. 新证书先在备用路径校验 PFX 密码、私钥、SAN、有效期和 TLS 1.2 握手，再安排短暂重启窗口。
2. 备份当前 PFX 文件和对应受保护密码引用；切换 `TlsCertificatePath` 后重启并验证 PC/Android 登录与 KeepAlive。
3. 握手失败或证书不匹配时，恢复上一份仍在有效期内的 PFX 与路径，重启回滚；不得临时开启公网 V1 绕过故障。
4. 监控证书到期日，至少提前 30 天告警；轮换后确认旧证书不再被引用。客户端和服务器系统时钟必须启用可靠的时间同步。

## V1 迁移期限

- 公网、`0.0.0.0` 和 `IPv6Any` 始终禁止 V1 明文。
- 回环或明确私网迁移期间可按需保留 V1；最后停止日期为 **2026-12-31**。到期前将所有客户端切换到 V2，并把 `AllowLegacyV1=false` 固化。

