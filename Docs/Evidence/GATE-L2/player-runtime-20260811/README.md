# GATE-L2 玩家入口与主题运行时证据（2026-08-11）

## 可运行工件

- 本地工件：`artifacts/gate-l2-current-only-20260811/LyoCrystal-Player-GATE-L2-self-contained.exe`
- 大小：59,506,669 字节（56.75 MiB，小于 80 MiB）
- SHA-256：`E8583162D3069636D7DC8F68854ECA43CBB58A61F1B194C5C19C43C90E140E0D`
- 结构：Native AOT 玩家外壳 + 自包含 Windows x64 客户端载荷；GM 与玩家机器不需要安装 .NET、SDK 或 C++ 工具链。
- 重命名验证：复制为 `传奇登录器-任意名称.exe` 后执行 `--shell-smoke`，退出码 0；验证目录内仅有该 EXE。
- 真实载荷验证：通过重命名后的单 EXE 转发 `--theme-render-smoke`，退出码 0，并生成本目录中的 12 张主题截图。

`artifacts/` 按仓库规则不入 Git；本页记录摘要和复现命令，测试结果与截图作为可审查证据入库。

## 自动化结果

- `player-integration.trx`：Release 集成测试 44/44 通过。
- 覆盖：三层签名快照回退、三套模板、两种区服模式、设置注册表、公告、微端主备切换、真实下载进度、缓存边界、客户端能力标记、单 EXE 打包与更新恢复接缝。
- 双重代码审查：Standards 与 Spec 最终复核均未发现新的 GATE-L2 代码问题。

## 复现命令

```powershell
dotnet publish Client_VorticeDX11\Client_VorticeDX11.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o artifacts\gate-l2-current-only-20260811\payload-core
```

```cmd
call C:\BuildTools\Common7\Tools\VsDevCmd.bat -arch=x64 -host_arch=x64 && dotnet publish Launcher.PlayerShell\Launcher.PlayerShell.csproj -c Release -r win-x64 --self-contained true -p:IlcUseEnvironmentalTools=true -o artifacts\gate-l2-current-only-20260811\player-shell-aot
```

```powershell
dotnet run --project Tools\LauncherPlayerPackager\LauncherPlayerPackager.csproj -c Release -- create artifacts\gate-l2-current-only-20260811\player-shell-aot\LyoCrystal.PlayerShell.exe artifacts\gate-l2-current-only-20260811\payload-core artifacts\gate-l2-current-only-20260811\LyoCrystal-Player-GATE-L2-self-contained.exe Client.exe artifacts\GATE-L0\brand-gate-l0.json
```

## 外部设备验收边界

当前机器只有一个 1920×1080、96 DPI 显示器。入库截图来自真实 PerMonitorV2 窗口在 96/120/144/192 DPI 消息后的直接捕获，不是二次缩放位图；它证明布局与命中区域的代码路径，但不能替代四档物理 Windows 缩放或真实跨屏演练。

因此物理 100%、125%、150%、200% 与跨屏验收保留到 GATE-L5 的 Windows 10 / Windows Server 双机交付演练；本阶段不伪造物理设备证据。

## 固定兼容边界

自动查找和手动选择只接受带有效 `launcher-capabilities.json` 的当前 15 参数客户端。未标记旧客户端只读拒绝，不写入、不升级，也不会用 DLL 字符串猜测能力。旧客户端原地迁移需要持久化多文件事务和真实旧版夹具，未作为 GATE-L2 的隐式行为开放。
