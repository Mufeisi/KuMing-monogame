# GATE-L5 本机运维与工件证据（2026-08-11）

## 验证边界

- 本轮机器：Windows 10 x64，版本 `10.0.19045.6693`。
- 本轮完成代码回归、自包含发布、无 SDK 生成、真实 URLACL/防火墙、Windows Service 安装/重启/卸载和恢复验证。
- Windows Server 2016 x64 双机链路和物理 100%/125%/150%/200% DPI 演练仍需外部机器，未以本机模拟结果替代。

## 自动化结果

- `gate-l5-micro-final.trx`：11/11 通过；覆盖稳定文件索引、成熟后重复重扫、失败沿用旧索引、缓存损坏重建、容量约束、单键合并、100 并发 Range 与内存边界。
- `gate-l5-launcher-final.trx`：63/63 通过；保持启动器编辑器、签名发布、玩家入口和既有更新链回归。
- `dotnet build MicroGateway.App/MicroGateway.App.csproj -c Release --no-restore`：0 警告、0 错误。
- Standards 与 Spec 最终双轴复审均未发现仍存代码硬问题。

## 自包含工件

| 工件 | 字节 | SHA-256 |
| --- | ---: | --- |
| `gateway-template-final2/MicroGateway.App.exe` | 116579789 | `AC89B7FD80DD25688142D6AF3E549937323938AF869483EED5E73F0FDACE603B` |
| `MicroGatewayTemplate-final2.zip` | 46196326 | `32A58418663B41320BB1844E30F00ACA6A8DD9DC91B756C9911330EA38AF9664` |
| `editor-final2/LyoCrystal.LauncherEditor.exe` | 221335674 | `2C72A0F9D8E3FADCBCBC937008BE175674EA8332A0D0D3D8A4F9E9A2F303154B` |
| `delivery-final2/smoke-project-玩家入口.exe` | 59711527 | `DBA46325F8A446FE7B8F0D8FDFC52D1438818225EBDE3C5C00894A2E38E9C14C` |
| `delivery-final2/smoke-project-微端网关.zip` | 103201882 | `672CEFAF03C0896BBCDE3D98CABBB677F7547C024EF0C0BFC74307A6F26F3FA8` |

编辑器在仅保留 Windows `System32` 的 `PATH` 且 `DOTNET_ROOT` 指向不存在目录时执行 `--editor-smoke`，退出码为 0，并生成玩家单 EXE、微端包、签名发布目录、离线发布包和恢复包。解压后的网关在相同无 SDK/运行时环境执行 `--gateway-smoke`，退出码为 0；一次性 `gateway-secret.import` 已消费删除，安装 Service 前不会产生 LocalMachine 服务密钥。

## 真实 Windows 运维演练

测试项目为 `smoke-project`，监听 `http://+:8080/`，服务名 `LyoMicro_341A79710BB018DF`。

1. 提权配置网络成功：URLACL 同时包含项目 Service SID 与发起 GUI 用户 SID；创建唯一命名 TCP 入站规则。
2. 提权安装服务成功：服务进入 `RUNNING`，状态文件报告 1644 个索引文件、1268678430 字节资源。
3. 服务宿主健康检查返回 200；带 User/Code 的真实 Range 请求返回 206、32 字节，`Content-Range` 为 `bytes 0-31/18921965`。
4. 写入本地重扫请求后，服务消费并删除请求文件，索引版本由 2 递增至 3，文件数保持 1644。
5. 服务停止后重新启动；首次全量索引期间为 `START_PENDING`，稳定后转为 `RUNNING`，健康检查再次返回 200。
6. 提权卸载成功：服务不存在，安装事务文件与服务密钥均已删除。
7. 提权撤销网络成功：事务文件删除；复查 URLACL 与防火墙规则均不存在；测试项目写入的当前用户凭据也已删除。

## 尚未关闭的外部验收

- 在独立 Windows Server 2016 x64 微端机与 Windows 10 x64 游戏服/玩家机之间执行四路由、Range、断网恢复、上传稳定隔离和完整 GM 到玩家链路。
- 在目标机器执行 Windows Service 开机自启、多实例和网络事务强停恢复演练。
- 采集 100 个并发真实公网/局域网流式请求的进程内存曲线；现有自动化仅证明本机工作集增量门槛。
- 使用真实 100%/125%/150%/200% 显示缩放或物理跨屏完成 DPI 截图与点击区域验收。

在以上外部证据归档前，不宣称 GATE-L5 完整关闭。

## 语言

本证据、状态和交付说明使用中文；英文仅保留代码标识符、命令、哈希和系统原始状态名。
