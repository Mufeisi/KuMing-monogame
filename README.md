# LyoCrystal
水晶传奇三端版，C#脚本驱动带热更，带移动端，使用微端更新。
</br>
</br>

## BASE-02 可复现构建基线

仓库用 `global.json` 固定稳定 .NET SDK `10.0.200`（`rollForward=disable`，禁止 Preview）。先确认 SDK，再校验仓库内已提交资源：

```powershell
dotnet --version
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope Repository
```

资源版本、目录树 SHA256、来源和缺口记录在 [`resources.manifest.json`](resources.manifest.json)。当前仓库只包含 `Client_MonoGame.Shared/Assets/UI` 与 `Client_MonoGame.Shared/Content`；`BootstrapAssets`、PC `Data/Map/Sound`、地图样本和补丁仓库仍是外部输入。README 不提供未知 QQ 文件，也不伪造外部哈希。`.gitattributes` 将纳入哈希的文本资源固定为 LF，避免 Windows `core.autocrlf` 造成 fresh checkout 漂移。

从 QQ 群共享（群号 `1063081017`）或其他已授权来源取得资源后，将目录按清单中的相对路径镜像到一个临时目录，例如：

```text
<资源镜像>/Client_MonoGame.Shared/BootstrapAssets/...
<资源镜像>/Build/Client_VorticeDX11/...
<资源镜像>/Build/Mobile/BootstrapRepo/...
```

外部条目的 `sha256` 目前明确为 `null`，因此它们仍是阻塞项。拿到来源方提供的确切版本和哈希后，先人工写入清单；脚本不会猜测或生成未知哈希。然后使用唯一获取链：脚本会先验证镜像结构和固定哈希，再复制到仓库内临时目录、复验、拒绝非空目标并安全替换，最后只校验仓库目标：

```powershell
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 `
  -Action Acquire -Scope All -ExternalRoot C:\path\to\authorized-resource-mirror
```

获取成功后（或只想复核当前仓库目标时）执行：

```powershell
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope All
```

未提供外部资源、哈希仍为 `null`、源目录与仓库重叠、源含 reparse point、或目标目录非空时，命令都会以非零退出并列出阻塞原因。`Validate -Scope All` 不读取外部镜像，只验证实际仓库目标。

当 `BootstrapAssets/bootstrap-packages.json` 已通过校验，可复用现有分包导出入口生成补丁仓库（脚本会在资源缺失时直接失败）：

```powershell
pwsh -NoProfile -File Tools/Mobile-BootstrapPackageRepoExport.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -OutputRoot (Join-Path (Get-Location).Path 'Build/Mobile/BootstrapRepo')
```

该基线不提前执行 BASE-06/BASE-07 的 TFM 迁移；移动端当前仍为 `net11.0-*`，Server/PC 仍为 `net8.0-*`。在没有外部资源时可以构建不依赖资源的项目，例如：

```powershell
dotnet build Shared/Shared.csproj
dotnet build Server/Server.Library.csproj
dotnet build Tools/MobileBootstrapAudit/MobileBootstrapAudit.csproj
```

构建警告（例如现有 NuGet 漏洞或 nullable 警告）不属于 BASE-02 资源基线，需在对应阶段处理。

---

移动端完成度相对较低，仍然在开发中。。
</br>
</br>
升级记录：</br>
1、PC客户端图像渲染升级VorticeDX11；</br>
2、废弃自管理二进制数据库，迁移Sqlite + MySQL切换；</br>
3、废弃txt脚本，迁移C#脚本，预留所有业务逻辑脚本化能力；</br>
4、增加C#自动热更（FileWatcher） + 手动推送；</br>
5、增加C#脚本调试，增加变量单步跟踪；</br>
6、增加AI辅助生成C#脚本；</br>
7、客户端增加平滑移动；</br>
8、客户端增加视角缩放；</br>
9、增加安卓+iOS移动端（Xamarin + monogame），实现三端互通；</br>
10、移动端自绘NPC对话、商店、任务、商城、任务、好友、邮件、组队、大地图；</br>
11、增加移动端微端 + 移动端版本管理；</br>
12、移动端废弃自绘HUD，改为FairyGUI；</br>
13、增加PC端微端，PC端修改版本管理逻辑（与移动端保持一致）；</br>
14、移动端升级MAUI，使用AOT编译；</br>
15、移动端瘦身初始资源包，只保留初屏资源和系统自带库，apk体积1.3G降到85M；</br>
</br>
</br>
相关资源文件在QQ群共享文件中，群号：1063081017</br>
</br>
</br>
<img src='https://github.com/AndrewChien/LyoCrystal/blob/main/Docs/Pics/%E7%95%8C%E9%9D%A2.png'/></br>
<img src='https://github.com/AndrewChien/LyoCrystal/blob/main/Docs/Pics/%E8%83%8C%E5%8C%85.png'/></br>
<img src='https://github.com/AndrewChien/LyoCrystal/blob/main/Docs/Pics/%E7%8A%B6%E6%80%81.png'/></br>

---

# 传奇技术交流

&emsp;&emsp;我创建了一个交流QQ群，欢迎感兴趣的小伙伴们的加入~</br>

<img src='https://github.com/AndrewChien/Blog/blob/master/source/20251128111124_22_95.jpg'/></br>
