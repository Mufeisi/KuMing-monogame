# GATE-L0 Native AOT 玩家入口验收证据

## 结论

GATE-L0 已具备关闭条件。本机原有 MSVC 与 Windows SDK 完整，但 Visual Studio 实例未被 `vswhere` 登记；通过现有 `VsDevCmd.bat` 加载开发环境并设置 `IlcUseEnvironmentalTools=true` 后，真实 Native AOT 发布成功，不需要重新安装工具链。

## 最终可运行工件

- 路径：`artifacts/gate-l0/LyoCrystal-Player-GATE-L0-final.exe`
- 文件大小：18,329,181 字节（17.48 MiB），低于 80 MiB 上限；
- SHA-256：`1DE1E1FD3897D3FA4FF0AE747642CFB560A6AB0B3157BBA828E20B9AF6ADEDC5`；
- 品牌：产品名 `LyoCrystal 玩家入口`，说明 `LyoCrystal 单文件玩家入口`，公司 `LyoCrystal`，版本 `1.0.0.0`；
- 内嵌启动核心：920 个文件，原始 55.92 MiB，压缩载荷 13,975,037 字节；
- 内嵌载荷 SHA-256：`74eee39fe75feaa270862a36b08a7c6be6c29de1cb544809d7e167787660b71e`。

启动核心来自现有 `Build/Client_VorticeDX11`，明确排除由微端按需提供的 `Data`、`Map`、`Sound` 和运行缓存 `Cache`。玩家 EXE 不包含完整 8.21 GiB 资源库，也不提供下载全部资源入口。

## 运行验收

- 复制并重命名为 `传奇登录器-任意名称.exe` 后，`--shell-smoke` 退出码为 0；
- 重命名目录只有这一个 EXE，没有同目录 DLL、INI、图片或脚本；
- 正常运行时玩家入口退出码为 0，并从内嵌载荷启动：
  `C:\Users\luo\AppData\Local\LyoCrystal\PlayerPayloads\74eee39fe75feaa270862a36\Client.exe`；
- `dumpbin /dependents` 仅列出 Windows 系统库和 Universal CRT API Set，没有项目 DLL 或 .NET 运行时依赖；
- `aot-final-publish.log` 未出现 IL2026、IL3050 或发布错误。

## 强停恢复

`strong-kill-real-final.trx` 强制要求两个不同摘要的 17.48 MiB 最终玩家 EXE，缺少任一输入时门禁失败。测试使用仓库构建的 net10 工作进程，在协调器的精确中断点暂停并强制终止，不依赖 PowerShell 7。三个故障点分别为：

- v1：`1DE1E1FD3897D3FA4FF0AE747642CFB560A6AB0B3157BBA828E20B9AF6ADEDC5`；
- v2：`45A1BEADFC44DA7ECAAD78E9C9E87C2C103CE2355B79BC42CA130029249B76C9`。

1. 替换前强制终止；
2. 日志已持久化为 `Applying`、原子替换尚未发生时强制终止；
3. `File.Replace` 已完成、日志尚未提交时强制终止。

每组均由新的工作进程重新调用协调器恢复；恢复后正式入口均为完整 v2，`.previous` 均为完整 v1，摘要逐字节匹配。门禁专项 1/1、玩家外壳常规专项 8/8 通过。`strong-kill-missing-input.trx` 另行证明缺少真实 EXE 时不会静默退化为文本样品。

```powershell
$env:LYOCRYSTAL_GATE_L0_V1='artifacts\gate-l0\LyoCrystal-Player-GATE-L0-final.exe'
$env:LYOCRYSTAL_GATE_L0_V2='artifacts\gate-l0\LyoCrystal-Player-GATE-L0-final-v2.exe'
dotnet test WindowsIntegration\Launcher.PlayerShellGateL0\Launcher.PlayerShellGateL0.Windows.csproj -c Release
```

## 回归结果

- 远程列表：22/22，见本目录 `remote-list.trx`；
- HTTP 安全与四类微端协议：21/21，见本目录 `http-micro.trx`；
- 玩家入口常规专项：8/8，见 `player-shell-final-v2.trx`；
- 真实 EXE 强停门禁：1/1，见 `strong-kill-real-final.trx`；
- Base05 全量：369/370。唯一失败仍为既有 `Android签名恢复包可跨DPAPI文件往返且错误口令失败关闭`，期望退出码 2、实际未处理异常退出码 1；与本次 Shared JSON source generation 和玩家入口改动无关，结果见 `base05-full-after-aot.trx`。

## 构建命令

```cmd
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
dotnet publish Launcher.PlayerShell\Launcher.PlayerShell.csproj -c Release -r win-x64 --self-contained true -p:IlcUseEnvironmentalTools=true
```

构建机需要 MSVC 和 Windows SDK；GM 与玩家机器不需要 SDK、.NET 运行时或编译工具。

## 语言

证据说明使用中文；命令、类型名和原始报错保留英文。
