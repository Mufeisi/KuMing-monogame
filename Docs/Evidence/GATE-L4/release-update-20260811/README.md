# GATE-L4 签名发布、自更新与回滚验收证据

## 可直接运行工件

- GM 编辑器：`artifacts/gate-l4-release-20260811/editor-isolated-v2/GM启动器编辑器.exe`
  - 225,407,098 字节；SHA-256 `56A1985907CD38CF579F96973AAD9F17136833C9324537CFCD7B6F3D9D16B9BF`。
  - 单目录只有一个 EXE；在 `PATH` 仅含 Windows 系统目录且 `DOTNET_ROOT` 指向不存在目录时，`--editor-smoke` 退出码为 0。
- 编辑器生成的玩家入口：`artifacts/gate-l4-release-20260811/delivery-smoke-v2/smoke-project-玩家入口.exe`
  - 59,742,860 字节（56.98 MiB），低于 80 MiB 上限；SHA-256 `9603F5BA46461DCB9927BE593DBFB31C1E772552D6C1375ABB980F1A5533F2B9`。
  - 原名及复制重命名后的 `--shell-smoke` 均为 0，不需要另装 .NET Desktop Runtime。
- 独立微端包：`artifacts/gate-l4-release-20260811/delivery-smoke-v2/smoke-project-微端网关.zip`
  - 107,250,240 字节；SHA-256 `529398CC243709FAE5B8F2CBBF99C0852BA0752FE446021AA727A73554E1BC89`。
- 签名离线发布包：`artifacts/gate-l4-release-20260811/delivery-smoke-v2/smoke-project-离线发布.zip`
  - 57,015,158 字节；SHA-256 `6AB651B93C66832EA4DEB1F1ECD69FB1B2089707DACFE48299ED5EDEBD4B4B7F`。

## 发布与恢复闭环

- 每个项目自动生成当前/下一 ECDSA P-256 密钥；私钥使用当前 Windows 用户 DPAPI 保存，不写入项目 JSON、玩家 EXE、网关包或普通配置。
- 独立密码的 AES-GCM 恢复包可在私钥缺失后恢复；错误密码、项目身份不符及不同私钥覆盖均拒绝。
- 发布目录使用 `current.txt → versions/<不可变版本>`；并发发布使用项目发布根命名互斥，序列同时从项目元数据和当前已签名版本恢复下限。
- 历史回滚先复验历史 manifest、项目历史摘要和每个文件，再以更高序列重新发布；被篡改历史不会被重新签名。
- 离线导入会验签、检查完整性，并拒绝低序列或同序列不同摘要；同一签名版本只允许幂等导入。
- 密钥轮换使用逐跳签名信任链；退休键带 `NotAfterSequence`，旧私钥不能签署退休窗口之后的新版本。

## 玩家入口更新与强停恢复

- 普通更新在后台下载；检查或下载失败继续当前完整入口。
- 强制更新在下载前原子保存已验签门槛；失败及后续离线启动持续阻止进入，直到安装相同或更高版本。后续普通高版本若满足门槛，也按阻断模式完成替换。
- 新入口只写入当前 EXE 同卷的 `.new`，摘要和长度通过后才登记签名替换日志；Native AOT 外壳在下一次启动处理原子替换并保留 `.previous`。
- 多个游戏实例分别登记 PID 和进程启动时间；任一实例仍运行时推迟入口替换，登记失败会终止刚启动的游戏而非静默放行。
- 两个真实玩家 EXE 在替换前、日志已持久化、原子替换后三个确定点强停并复启，1/1 通过，见 `gate-l4-real-exe-strong-kill.trx`。

## 三类发布源与自动测试

- 真实静态 `HttpListener` 和独立 `MicroHttpListenerHost /launcher/` 分别提供同一不可变签名版本；两条链路均完成 HTTP 下载、验签、Accepted/LKG 原子落盘。
- 生成的独立微端包真实执行健康检查、User/Code 鉴权和 Range 读取，退出码为 0；一次性 `gateway-secret.import` 导入后自动删除。
- 签名离线 ZIP 经断开网络的目录导入路径完成验签和安装；离线包不含私钥。
- 玩家入口、主题、编辑器、发布、信任链、入口更新和真实 HTTP/微端集成测试 63/63，通过，见 `gate-l4-delivery-63.trx`。
- Standards 与 Spec 两轴最终复审均无剩余硬问题。

## 截图与边界

- `screenshots/editor-ui.png`：正式自包含编辑器运行界面。
- `screenshots/editor-preview.png`：共享主题渲染模块的实时预览。
- Windows 10 x64、Windows Server 2016 x64 双机完整交付和物理 DPI 演练仍按计划放在 GATE-L5，不在本机伪造成双机证据。

## 语言

证据说明使用中文；代码标识符、命令参数和协议路径保留原文。
