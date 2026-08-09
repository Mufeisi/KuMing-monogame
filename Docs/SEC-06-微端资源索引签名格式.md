# SEC-06 微端资源索引签名格式

## 结论与边界

PC 与 Mono/Android 的远端 `Packages/bootstrap-package-index.json` 必须先通过同一套签名、兼容版本和防降级校验，之后才能生成更新队列。未签名、未知字段、未知密钥、篡改、过期密钥、低序列以及最低兼容版本过高的索引均失败关闭，不进入下载和安装。

SEC-06 只固定格式和客户端校验实现。生产私钥生成、托管、备份、CI 短暂取用、索引签名、生产公钥写入只读信任表和发布回滚属于 `RELEASE-01/02`。因此在 RELEASE-01 写入至少一把生产公钥前，远端自动更新会明确拒绝；壳内随 APK/PC 包交付的 baseline 索引只用于识别本地已安装版本，不作为远端更新授权。

## JSON 包装格式

字段名和大小写固定，未知字段或任意层级的重复字段均拒绝。示例中的 `Signature` 仅为占位：

```json
{
  "Format": "lyocrystal-bootstrap-index-v1",
  "Algorithm": "ECDSA_P256_SHA256_P1363",
  "KeyId": "resource-2026-a",
  "Sequence": 42,
  "GeneratedAtUtc": "2026-08-10T12:00:00Z",
  "ResourceVersion": "content-20260810.42",
  "MinimumClientVersion": "1.0.0",
  "Packages": [
    {
      "Name": "core-startup",
      "Sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "Size": 54555646
    }
  ],
  "Signature": "Base64编码的固定64字节P1363签名"
}
```

约束：

- JSON UTF-8 内容不超过 8 MiB，资源包数量为 1～4096。
- `KeyId` 为 1～64 位 ASCII 字母、数字、点、下划线或短横线。
- `Sequence` 是大于零的 64 位整数；同一次发布不得复用旧序列。
- `GeneratedAtUtc` 是带 `Z` 的 UTC RFC3339 时间，签名载荷统一到 7 位小数。
- `ResourceVersion` 为 1～128 位 ASCII 字母、数字、点、下划线或短横线。
- `MinimumClientVersion` 使用 `System.Version` 可解析的数字版本；高于当前客户端时拒绝更新。
- `Name` 为 1～128 位 ASCII 字母、数字、点、下划线或短横线，按不区分大小写去重。
- `Sha256` 必须是 64 位小写十六进制；`Size` 不得为负数。
- `Signature` 为 ECDSA P-256、SHA-256、IEEE P1363 固定字段拼接格式，解码后恰好 64 字节。

## 确定性二进制签名载荷

签名不覆盖 JSON 原始字节，而覆盖解析后的确定性二进制载荷。整数均为大端序；字符串均为“4 字节无符号含义的非负长度 + 严格 UTF-8 字节”，不含终止符。资源包先按 `Name` 的 ordinal 升序排列，所以纯 JSON 排版和数组顺序变化不改变签名。

载荷顺序：

1. 固定 ASCII 魔数 `LyoCrystalBootstrapIndex\0`。
2. 4 字节格式版本 `1`。
3. 8 字节 `Sequence`。
4. `Format`。
5. `Algorithm`。
6. `KeyId`。
7. `ResourceVersion`。
8. 规范化后的 `GeneratedAtUtc`。
9. `MinimumClientVersion`。
10. 4 字节资源包数量。
11. 每个资源包依次写入 `Name`、小写 `Sha256`、8 字节 `Size`。

`Signature` 字段本身不进入载荷。实现入口为 `Shared.Security.BootstrapManifestSignaturePolicy.BuildCanonicalPayload`，发布签名端必须按上述字节规则产生完全相同的载荷。

## Key ID 与轮换

客户端只信任编译进发行物的 SPKI 公钥，不接受远端索引携带新公钥。每个可信项包含：

- `KeyId`；
- Base64 编码的 DER `SubjectPublicKeyInfo`；
- `NotBeforeSequence`；
- `NotAfterSequence`，零表示无上限。

轮换时先发布同时信任“当前/下一”两把公钥的客户端，再从下一把密钥的起始序列开始签名。旧密钥的 `NotAfterSequence` 到期后，即使签名数学上正确也拒绝。私钥泄露时必须发布移除或收紧该 Key ID 窗口的新客户端；远端不能自行恢复信任。

## 防降级和失败行为

每端在各自应用私有运行时目录原子保存 `BootstrapManifestSecurityState.json`，记录最高已接受 `Sequence`、`ResourceVersion` 和 `KeyId`：

- 低于最高序列：拒绝。
- 等于最高序列但资源版本不同：拒绝。
- 等于最高序列且资源版本相同：允许幂等重试。
- 防降级状态存在但损坏：失败关闭，不当作首次安装。
- 旧的、没有已接受签名状态绑定的更新队列：忽略，不继续下载。
- 签名或状态验证失败：更新计划返回失败；不会把远端内容缓存成已接受索引，也不会生成更新队列。

卸载/清除应用数据会同时清除本地最高序列，这是新安装边界；RELEASE 阶段若要求跨重装保持版本地板，应把最高序列接入平台受保护且可恢复的设备状态或在线发行策略。

## RELEASE-01 接入清单

1. 离线生成独立于 APK 密钥的 P-256 资源签名密钥，私钥进入受保护密钥系统。
2. 将当前与下一把公钥及其序列窗口写入 `BootstrapManifestTrustConfiguration.TrustedKeys` 后重新发布客户端。
3. 发布流水线从受保护存储短暂取用私钥，构造确定性载荷并写入 64 字节 P1363 签名。
4. 发布前执行 T-07：正确签名、资源哈希篡改、未知 Key ID、旧/过期密钥、低序列、同序列异版本、最低版本和事务回滚。
5. 只有签名索引与全部 ZIP/SHA-256 工件同时上传成功后，才能原子切换仓库入口。
