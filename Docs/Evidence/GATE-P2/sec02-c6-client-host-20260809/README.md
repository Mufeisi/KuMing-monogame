# SEC-02 C6 客户端 TLS 宿主证据

## 范围

- Windows 宿主直接引用 PC 正式客户端程序集，调用生产 `Network.Connect`。
- Android 复用正式 `Settings` 与 `Network`，只增加显式 Intent 探针，不发送测试账号。
- 不新增证书绕过，不触碰系统信任库，不包含证书固定或 SEC-03～06。

## 退出工件

- PC Windows 宿主：1/1，通过真实 TLS 端口连接并拒绝不受信证书，连接状态清空且未降级 V1。
- Android 探针入口：`sec02TlsHostProbe=true`，可用 Intent 临时传入 host/port/serverName；运行后恢复原设置。
- 逍遥旧端点诊断：`SEC02_TLS_HOST_PROBE:FAIL:网络端点;SocketException:Connection refused`。
- 逍遥黑洞/抖动端点诊断：稳定返回 `FAIL:握手超时;OperationCanceledException:12秒内未完成TLS握手`，迟到回调不会覆盖分类。
- 逍遥受信端点烟测：`www.cloudflare.com:443` 返回 `SEC02_TLS_HOST_PROBE:PASS`；没有证书链或在线吊销兼容性故障。
- Android Release arm64 AOT：构建成功并安装到 `127.0.0.1:21503`。
- TLS 专项：19/19；Base05 全量：230/230。
- TLS 成功握手与 Packet 往返继续由 `TlsTransportTests` 的严格证书链专项覆盖。
