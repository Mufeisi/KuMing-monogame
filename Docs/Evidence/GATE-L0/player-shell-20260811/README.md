# GATE-L0 玩家入口外壳基础证据

## 当前结论

本目录证明玩家入口外壳的仓库内实现与既有协议基线已经通过快内环，但 **不证明 GATE-L0 已关闭**。当前构建机缺少 Native AOT 在 Windows 上要求的 C++ Desktop 链接器，因此尚无可用于最终验收的 Native AOT 单 EXE。

## 已验证工件

- `player-shell.trx`：8/8 通过。覆盖载荷追加、重命名后读取、流式解包、解包缓存篡改拒绝、80 MiB 上限、Windows 版本与 ICO 品牌资源、签名替换及中断状态恢复。
- `remote-list.trx`：22/22 通过。固化现有远程启动清单、缓存与本地保底行为。
- `http-micro.trx`：21/21 通过。其中既有 HTTP/管理/开关/备份专项 20 项，新增真实 `HttpListener` 微端协议夹具 1 项，覆盖文件 Range、图库头、单图和声音。
- `native-aot-publish.log`：Native AOT 发布失败的原始 MSBuild 日志。失败点为 `Platform linker not found`，日志明确要求安装 Visual Studio `Desktop Development for C++` 工作负载。

## 尚未满足的退出条件

- 构建并运行真实 Native AOT 品牌外壳；
- 附加完整 PC 客户端与内置快照后得到不超过 80 MiB 的玩家 EXE；
- 无辅助文件启动、任意重命名启动；
- 对最终 EXE 执行替换前、原子替换附近、替换后强停恢复演练；
- 保存最终 EXE 的大小、SHA-256、Windows 品牌资源与运行输出。

在上述证据齐全前不得进入 GATE-L1，也不得将普通 .NET bundle 回退样品作为 Native AOT 验收物。

## 语言

证据说明使用中文；命令、类型名和原始报错保留英文。
