# LyoCrystal
水晶传奇三端版，C#脚本驱动带热更，带移动端，使用微端更新。

## 开始开发

仓库使用 Windows PowerShell、Visual Studio 2022 或 `dotnet` CLI，SDK 版本由 [`global.json`](global.json) 固定。首次进入仓库先执行不依赖外部游戏资源的快速内环：

```powershell
dotnet --version
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope Repository
dotnet restore LyoCrystal.Server.slnf
dotnet build LyoCrystal.Server.slnf --configuration Release --no-restore
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --configuration Release
```

按工作内容选择入口：

| 工作内容 | 入口 |
|---|---|
| 服务端 | [`LyoCrystal.Server.slnf`](LyoCrystal.Server.slnf) |
| PC、管理工具与发布基线 | [`LyoCrystal.Windows.slnf`](LyoCrystal.Windows.slnf) |
| 启动器与微端 | [`LyoCrystal.Launcher.slnf`](LyoCrystal.Launcher.slnf) |
| 全部工程与架构视图 | [`Legend of Mir.sln`](Legend%20of%20Mir.sln) |
| 环境、构建、测试、调试与产物 | [`Docs/开发者指南.md`](Docs/开发者指南.md) |
| 工程规范与贡献流程 | [`CONTRIBUTING.md`](CONTRIBUTING.md) |
| 文档与当前治理任务 | [`Docs/index.md`](Docs/index.md) |

完整客户端运行依赖受授权的外部资源，不等同于上述源码快速内环。不要把本机资源、密钥、运行数据库或个人 `.slnLaunch.user` 提交到仓库。

首轮项目整理已经完成并进入维护态：工程入口、模块导航、文档事实源、CI 和远端分支保护均已建立。后续不再以“整理项目”为由批量移动目录或拆分类；新功能、修复和优化应先明确活动任务，并在实际触达模块时按规范渐进改善。当前状态入口见 [`Docs/工程治理实施路线.md`](Docs/工程治理实施路线.md)。

> ENG-12 正在按 [`ADR-0037`](Docs/adr/0037-物理目录采用分批迁移.md) 将源码逐步收敛到 `src/`、`Tests/`、`Tools/` 等稳定入口。迁移期间以解决方案和本 README 的命令为准；本机被忽略的 `artifacts/`、`bin/obj`、运行配置和秘密目录不会由迁移任务自动删除。

## BASE-02 可复现构建基线

仓库用 `global.json` 固定稳定 .NET SDK `10.0.200`（`rollForward=disable`，禁止 Preview）。先确认 SDK，再校验仓库内资源：

```powershell
dotnet --version
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope Repository
```

资源版本、目录树 SHA256、来源和三阶段摘要记录在 [`resources.manifest.json`](resources.manifest.json)。外部资源条目显式区分 `source`（授权源）、`acquired`（获取/overlay 后）和 `final`（导出器处理后）；脚本不会猜测或生成未知哈希。`.gitattributes` 将纳入哈希的文本资源固定为 LF，避免 Windows `core.autocrlf` 造成 fresh checkout 漂移。

清单中的每个非空来源都必须有稳定 `source.id`、`source.locator`、版本和 `versionSha256`，并声明 `source.acquisition`（获取入口）与 `source.validation`（SHA256 目录摘要契约）。`resource.version` 严格等于 `source.version`，`source.versionSha256` 是规范化 version 文本的 SHA256；`source.validation.phase` 必须绑定同阶段目录摘要（`repository/generated` 绑定 `final`，授权本地源绑定 `source`）。摘要覆盖实际复制的全部文件；PC 运行资源的 fresh 源为 6671 个文件，加入 `Mir2Config.ini` overlay 后为 6672 个。构建链接或其它生成物出现在该目录会被摘要计入并导致漂移，fresh Acquire 后应在构建前先验证基线。文件或来源哈希、阶段计数/大小发生漂移时，`ResourceBaseline.ps1` 以非零退出。补丁仓库另校验 `core-startup.zip`、包索引及其 `.sha256` sidecar，防止包文件和清单脱节。

当前授权镜像根为 `D:\ChuanQi\客户端`：移动资源位于 `monogame`，PC 资源位于 `Client_VorticeDX11`。不修改该源目录。BASE-02 的唯一可复现链是：先获取并校验 `acquired`，移动资源再运行现有分包导出器，最后校验 `final`；PC 资源没有隐藏的 finalize 步骤，清单明确 `acquired=final`，因此 Acquire 后即可进入最终校验。

首次从 Git fresh clone 执行 Acquire 时，`Client_MonoGame.Shared/BootstrapAssets` 已包含 273 个仓库追踪文件。脚本只在该目录当前内容与清单 `repositoryOverlay` 摘要精确一致时允许 overlay；任何漂移或额外文件都拒绝。没有 `repositoryOverlay` 的其它目标必须不存在或为空。BASE-02b 的 CI 裸克隆资源镜像仍是独立 backlog，本 README 不将本机 QQ 群资源声明为 BASE-02b 完成。

```powershell
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 `
  -Action Acquire -Scope All -ExternalRoot D:\ChuanQi\客户端

pwsh -NoProfile -File Tools/Mobile-BootstrapPackageRepoExport.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -OutputRoot (Join-Path (Get-Location).Path 'Build/Mobile/BootstrapRepo')

pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope All
```

`Acquire` 会先验证 `source`，把资源和声明的 overlay 复制到仓库内临时目录，验证 `acquired` 后再替换空目标或精确匹配的 repository overlay；overlay 目标必须在获取前已存在，缺失即拒绝。替换阶段保留旧目标备份；在所有新目标替换且替换后验证成功时到达唯一提交点，提交点前任一移动、注入故障或验证失败都会按逆序删除新目标并恢复旧目录。提交点后的备份清理属于 post-commit cleanup，清理失败不会回滚新目标，而是保留备份目录并报告人工恢复路径；提交前回滚自身失败也会保留暂存目录和备份路径。移动资源的导出器会规范化 `bootstrap-package-index.json` 并生成补丁仓库，随后 `Validate All` 验证所有 `final` 摘要并逐包交叉校验 ZIP、索引和 SHA256 sidecar。`source.type=none` 仅用于可选缺口，必须声明 `version=absent`、`versionSha256` 和 `validation.method=assert-absent`、`validation.scope=target-absent`，目标一旦出现文件或目录即失败；不支持 `skip`。源与仓库重叠、reparse point、非 overlay 目标非空、overlay 漂移或任一摘要不匹配时命令都会以非零退出。

只需复核当前仓库目标时，可单独执行：

```powershell
pwsh -NoProfile -File Tools/ResourceBaseline.ps1 -Action Validate -Scope All
```

BASE-06 的移动端代码迁移与本机构建已完成，模拟器 Debug/Release/AOT+Trim/Trim-only 四态已通过；移动端当前为稳定 `net10.0-*`。BASE-08 = GATE-P0 已完成，证据见提交 [`4436426`](https://github.com/Mufeisi/KuMing-monogame/commit/443642644bc709a6059caaa94d84dc7a2eee15fd) 及 [GitHub Actions run 31081000003](https://github.com/Mufeisi/KuMing-monogame/actions/runs/31081000003)。P0～P5 已完成并转入维护；工程治理状态只在 [`Docs/工程治理实施路线.md`](Docs/工程治理实施路线.md) 维护，产品任务以开工时明确指定的活动 PRD、Issue 或实施规格为准，完整生命周期规则见 [`Docs/index.md`](Docs/index.md)。在没有外部资源时可以构建不依赖资源的项目，例如：

BASE-09 的 iOS TFM 已隔离：`Client_MonoGame.Shared` 默认只求值 `net10.0;net10.0-android`，iOS 工程通过 `EnableIosTarget=true` 显式求值 `net10.0;net10.0-ios`；Windows/Android restore/build 不再解析 iOS TFM。iOS 仍只做非门禁 restore，不承诺 iOS 编译或真机可玩。

```powershell
dotnet build Shared/Shared.csproj
dotnet build Server/Server.Library.csproj
dotnet build Tools/MobileBootstrapAudit/MobileBootstrapAudit.csproj
```

构建警告（例如现有 NuGet 漏洞或 nullable 警告）不属于 BASE-02 资源基线，需在对应阶段处理。

---

项目仍在持续演进；各端能力、历史验收与当前任务不得由本段升级记录推断，请以 [`Docs/index.md`](Docs/index.md) 指定的事实源为准。
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
