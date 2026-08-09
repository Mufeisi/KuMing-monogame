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

当前实现仍在启动进程中读取 `LYOCRYSTAL_TLS_CERT_PASSWORD`，仅作为 SEC-05 受保护密钥存储接入前的过渡；普通环境变量不是最终生产密钥存储，正式公网上线受 SEC-05 门禁阻塞。密码不写入 INI、日志或仓库。证书必须包含客户端使用的 DNS/IP SAN；客户端 `TlsServerName` 必须与 SAN 匹配。协议最低 TLS 1.2（可协商 TLS 1.3）。

## PC 与 Android 客户端

客户端 INI 的 `[Network]` 节使用独立 TLS 端口和名称：

```ini
UseTlsV2=true
TlsPort=7001
TlsServerName=game.example.com
TlsSpkiSha256Pins=sha256/当前证书公钥摘要;sha256/下一证书公钥摘要
```

`TlsSpkiSha256Pins` 固定的是证书公钥信息（SPKI）的 SHA-256 摘要，格式为 `sha256/<Base64>`；最多配置 4 项，以分号、逗号或换行分隔。空值表示不启用额外固定校验，正式发布配置应至少包含当前证书固定值。固定值校验是系统信任链、域名、有效期和在线吊销检查之外的附加条件，不能绕过这些检查。V2 握手失败不会降级 V1。错误提示会引导检查客户端系统时间、`TlsServerName`/证书 SAN、证书链、有效期和固定值；请先修正这些条件再重试。

可从 PEM/CRT 证书提取固定值（输出前加 `sha256/`）：

```bash
openssl x509 -in server.crt -pubkey -noout | openssl pkey -pubin -outform DER | openssl dgst -sha256 -binary | openssl base64
```

## 证书轮换、回滚与监控

1. 新证书先在备用路径校验 PFX 密码、私钥、SAN、有效期和 TLS 1.2 握手，并计算下一证书固定值。
2. 先发布同时包含“当前 + 下一”两个固定值的客户端配置，确认 PC/Android 均能连接当前证书；客户端覆盖率不足时不得切换服务端证书。
3. 备份当前 PFX 文件和对应受保护密码引用；切换 `TlsCertificatePath` 后重启并验证 PC/Android 登录与 KeepAlive。
4. 握手失败或证书不匹配时，恢复上一份仍在有效期内的 PFX 与路径，重启回滚；不得清空固定值或临时开启公网 V1 绕过故障。
5. 新证书稳定后再发布仅保留新固定值的客户端配置。监控证书到期日，至少提前 30 天告警；客户端和服务器系统时钟必须启用可靠的时间同步。

## V1 迁移期限

- 公网、`0.0.0.0` 和 `IPv6Any` 始终禁止 V1 明文。
- 回环或明确私网迁移期间可按需保留 V1；最后停止日期为 **2026-12-31**。到期前将所有客户端切换到 V2，并把 `AllowLegacyV1=false` 固化。
