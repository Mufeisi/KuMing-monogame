# GATE-L5 本机模拟完成与实机豁免记录（2026-08-11）

## 完成口径

用户明确授权：除必须实机执行的项目外，其余能力全部使用本机模拟、探针和自动化完成验证；达到只剩实机项目时，按本次任务完成并进入主线审查与合并。

本记录不声称下列物理测试已经运行，只记录它们被明确豁免为本次合并的非阻断项。

## 本机已覆盖

- 启动器编辑器多领域向导、三套模板、两种区服模式、素材优化开关与主题包导入导出。
- 外部网页公告成功探测与失败回退签名内置公告；固定安全动作白名单拒绝本地程序、脚本和非 HTTP/HTTPS 地址。
- 微端四类资源协议、Range、鉴权、Kill Switch、只读版本/资源状态、资源稳定索引、缓存和并发生命周期。
- 签名不可变发布、内容差异、回滚、防降级、目标微端离线导入与入口更新恢复。
- 玩家自包含单 EXE、受管理入口转交、无需额外 .NET Desktop Runtime、重命名冒烟和主题渲染冒烟。

## 获豁免的目标环境复验

- Windows 10 x64 游戏服与 Windows Server 2016 x64 微端服双机链路。
- 真实局域网/公网 100 并发请求内存曲线。
- Windows Service 开机自启、多实例及网络/服务事务强停恢复。
- 真实 100%、125%、150%、200% 系统 DPI 或物理跨屏拖动。

后续在目标环境执行时，应补充机器信息、配置摘要、命令、日志、截图、内存曲线与文件摘要；不能用本记录替代真实证据。

## 当前源码最终工件

- `artifacts/gate-final-simulated-20260811/smoke-final/smoke-project-玩家入口.exe`
  - 59,759,945 字节（56.99 MiB），低于 80 MiB。
  - SHA-256：`F86D9799EC1E60CE676BB08913C3F3DA9167698364305DCAC8003C5DB077E76D`。
  - 任意重命名后 `--shell-smoke` 退出码 0。
- `artifacts/gate-final-simulated-20260811/editor-complete/LyoCrystal.LauncherEditor.exe`
  - 221,448,826 字节（211.19 MiB），单文件自包含编辑器。
  - SHA-256：`16D96C2A3667151D60119898462904E371832AFAE2927B2AB42142E35563AFCB`。
  - `--editor-smoke` 退出码 0，实际生成玩家入口、签名三版本/回滚、离线包、恢复包、预览、界面截图和网关包。
- `artifacts/gate-final-simulated-20260811/smoke-final/smoke-project-微端网关.zip`
  - 103,235,354 字节（98.45 MiB），包含自包含网关与签名启动器发布内容。
  - SHA-256：`7747014386345B1CEA6140B6799747A85AA1C8395F728A72ED559A52C510A7D1`。
  - 解压后 `--gateway-smoke` 完成健康、鉴权和 Range，退出码 0。

## 自动化结果

- `Launcher.PlayerShellIntegration.Windows`：74/74 通过。
- 微端核心、服务端适配与协议筛选回归：13/13 通过。
- Base05 全量：381/382 通过；唯一失败仍是任务开始前已经隔离记录的 Android 签名恢复错误口令退出码差异（期望 2、实际未处理异常退出 1），本分支未修改该工具或测试。
- `Launcher.Editor`、`Launcher.PlayerShell`、`MicroGateway.App`：Release 构建成功且无新增警告；PC 客户端保持既有 WindowsBase 等警告，无新增错误。
