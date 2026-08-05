# LyoCrystal
水晶传奇三端版，C#脚本驱动带热更，带移动端，使用微端更新。
</br>
</br>

## BASE-02 可复现构建基线

仓库用 `global.json` 固定稳定 .NET SDK `10.0.200`（`rollForward=disable`，禁止 Preview）。先确认 SDK，再校验仓库内资源：

```powershell
dotnet --version
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope Repository
```

资源版本、目录树 SHA256、来源和三阶段摘要记录在 [`resources.manifest.json`](resources.manifest.json)。外部资源条目显式区分 `source`（授权源）、`acquired`（获取/overlay 后）和 `final`（导出器处理后）；脚本不会猜测或生成未知哈希。`.gitattributes` 将纳入哈希的文本资源固定为 LF，避免 Windows `core.autocrlf` 造成 fresh checkout 漂移。

当前授权镜像根为 `D:\ChuanQi\客户端`：移动资源位于 `monogame`，PC 资源位于 `Client_VorticeDX11`。不修改该源目录。BASE-02 的唯一可复现链是：先获取并校验 `acquired`，再运行现有分包导出器，最后校验 `final`。

```powershell
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 `
  -Action Acquire -Scope All -ExternalRoot D:\ChuanQi\客户端

pwsh -NoProfile -File Tools/Mobile-BootstrapPackageRepoExport.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -OutputRoot (Join-Path (Get-Location).Path 'Build/Mobile/BootstrapRepo')

pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope All
```

`Acquire` 会先验证 `source`，把资源和声明的 overlay 复制到仓库内临时目录，验证 `acquired` 后再原子替换空目标；移动资源的导出器会规范化 `bootstrap-package-index.json` 并生成补丁仓库，随后 `Validate All` 只验证 `final`。源与仓库重叠、reparse point、目标非空或摘要不匹配时命令都会以非零退出。

只需复核当前仓库目标时，可单独执行：

```powershell
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope All
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
