# SEC-02 C7 证书固定证据

## 交付结果

- 共享 TLS 客户端策略按 SPKI SHA-256 固定服务端公钥，格式为 `sha256/<Base64>`。
- PC、Mono 与 Android 正式连接均读取 `TlsSpkiSha256Pins`；支持当前/下一证书双固定值轮换，最多 4 项。
- 固定值只增加限制：系统信任链、域名、有效期或吊销检查出现错误时仍拒绝连接。
- 运维文档给出固定值提取、先发双值、再换服务端证书、最后移除旧值的顺序。

## 验证结果

| 验证 | 结果 |
|---|---|
| `TlsTransportTests` 专项 | 20/20 通过，见 `sec02-c7-tls.trx` |
| Base05 全量 | 239/239 通过，见 `sec02-c7-base05.trx` |
| PC `Client_VorticeDX11` Release | 0 错误，既有警告不阻断 |
| `Client_MonoGame.Shared` Release 多目标 | 0 错误，既有警告不阻断 |
| `Client_MonoGame.Android` Release | 0 错误，2 个既有可空性警告 |

专项覆盖正确固定值的真实 `SslStream` 握手和现有 Packet 往返、当前/下一双值、错误证书、系统证书错误以及格式和数量边界。全量测试耗时约 65 秒；Android Release 首次还原及 AOT 构建耗时约 147 秒。

## 命令

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~TlsTransportTests" --logger "trx;LogFileName=sec02-c7-tls.trx"
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-build --logger "trx;LogFileName=sec02-c7-base05.trx"
dotnet build Client_VorticeDX11/Client_VorticeDX11.csproj -c Release
dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -c Release
dotnet build Client_MonoGame.Android/Client_MonoGame.Android.csproj -c Release
```

## 边界

本证据只关闭 SEC-02。SEC-04～SEC-06 尚未完成，GATE-P2 仍未关闭；受保护证书密码存储由 SEC-05 负责。
