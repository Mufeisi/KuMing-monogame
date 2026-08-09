# SEC-06 微端资源索引签名证据

## 结论

- PC 与 Mono/Android 的远端 Bootstrap 索引均在进入更新队列前执行同一共享验签策略。
- 格式固定为严格 JSON 包装和确定性二进制载荷，签名为 ECDSA P-256、SHA-256、IEEE P1363 固定 64 字节。
- Key ID 信任、双密钥轮换窗口、最低兼容版本、单调序列和损坏状态失败关闭均已实现。
- 私钥、生产公钥注入与签名流水线仍属于 RELEASE-01/02；SEC-06 未越阶段实现发布系统。

## 可执行证据

- `sec06-signature.trx`：签名格式、轮换、篡改、降级、兼容版本、严格边界与持久化状态专项，`7/7` 通过。
- `sec06-base05.trx`：Base05 全量，`256/256` 通过。
- `dotnet build Client_VorticeDX11/Client_VorticeDX11.csproj -c Release`：0 错误。
- `dotnet build Client_MonoGame.Shared/Client_MonoGame.Shared.csproj -c Release -f net10.0`：0 错误。
- `dotnet build Client_MonoGame.Android/Client_MonoGame.Android.csproj -c Release -r android-arm64`：0 错误，包含 AOT/Trim Release 路径。

构建中的既有可空性、XML 注释和未使用字段警告未由 SEC-06 引入，不属于本任务退出条件。

## 覆盖范围

- JSON 数组顺序变化时确定性载荷保持一致。
- 当前/下一公钥按序列窗口平滑轮换，过期密钥拒绝。
- 哈希篡改、错误签名和未知 Key ID 拒绝。
- 低序列、同序列异资源版本拒绝；同序列同版本允许幂等重试。
- 最低客户端版本比较，缺失版本分量按零处理。
- 重复包、非小写 SHA-256、未知或重复 JSON 字段拒绝。
- 防降级状态原子落盘、重启后生效、损坏状态失败关闭。
- PC 与移动旧更新队列必须绑定已接受的签名资源版本。

## 边界

- 当前只读生产信任表按 RELEASE 边界保持为空，因此未注入生产公钥前远端自动更新失败关闭。
- 壳内 baseline 索引仅用于识别本地随包资源，不授权远端更新。
- 卸载或清除应用数据是防降级状态的新安装边界；跨重装版本地板留给 RELEASE 的受保护设备状态或在线发行策略。
