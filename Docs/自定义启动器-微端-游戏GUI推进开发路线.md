# 自定义启动器、微端与游戏 GUI 推进开发路线

| 项目 | 内容 |
|---|---|
| 文档状态 | 历史路线基线；启动器编辑器与独立微端 L0～L5 已完成，后续 GUI 能力须重新立项 |
| 日期 | 2026-08-12 |
| 适用仓库 | LyoCrystal |
| 前置状态 | `GATE-P0..P5` 已完成；本路线属于下一轮独立产品阶段 |
| 参考实现 | `91/JONE`、`188BLUEM2`、`GameOfMir_AppleM2` 等同类引擎源码 |
| 总目标 | 让版本作者能够低门槛完成启动器设计、微端配置、游戏内自定义窗口设计、测试发布与回滚 |

> 生命周期说明：本文保留阶段设计、先后关系和当时的“下一项”建议，但不再作为当前任务队列。已实施边界见《启动器编辑器与独立微端实施规格》及 `Docs/Evidence/GATE-L0..L5/`；尚未实施或新增的游戏 GUI 能力必须通过新的活动 PRD、Issue 或实施规格重新确认范围与验收，不能直接领取本文旧任务。

---

## 1. 为什么从这里开始

当前项目的生产与发布基础已经较完整，但版本作者的编辑体验明显落后于底层能力：

- `Launcher.Editor` 已能生成玩家入口、配置主题和区服、签名发布、比较版本与回滚，但可视化设计仍偏工程配置工具。
- PC 与移动端微端已经具备分包、签名、缓存、回滚和按需资源能力，但配置分散，版本作者难以直观看到“哪个区服入口、哪个资源版本”构成一个可发布发行体。微端服务程序本身是稳定基础设施，不属于每次启动器生成物。
- 移动端已内嵌 FairyGUI 运行时并实现大量固定业务窗口，但不存在面向版本作者的游戏 GUI 项目、设计器、受控数据绑定和动态窗口发布流程。
- `91/JONE` 的技术实现已老旧，但它贯通了“设计界面 → 保存项目 → 生成登录器 → 上传项目 → 客户端运行”的作者工作流，值得复刻其功能闭环。

因此本路线不重写已经存在的运行时和发布系统，而是补齐作者使用层，并按以下顺序推进：

```text
先修通启动器“选区 → 开始游戏 → 进入客户端”的真实链路
        ↓
再把启动器设计器做成熟
        ↓
把微端入口引用、资源预检和内容发布纳入同一工作流
        ↓
抽取经过真实使用验证的可视化设计核心
        ↓
用第二个 Adapter 接入游戏 GUI
        ↓
增加服务端权威数据和受控动作
```

这个顺序可以尽早交付用户可见工件，也避免先造一个未经实际编辑场景验证的通用设计器框架。

---

## 2. 参考源码中应借鉴的能力

### 2.1 `91/JONE` 启动器设计与生成

参考文件：

- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\M2ProjectBuilder\M2LoginEditor.pas`
- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\M2ProjectBuilder\M2MakeLoginFrm.pas`
- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\M2ProjectBuilder\M2LoginEmbFiles.pas`
- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\Common\uCliTypes.pas`

应复刻的功能：

- 画布式窗口和控件编辑。
- 控件树、属性检查器、资源选择。
- 图标、Logo、背景、按钮多状态、公告和区服列表配置。
- 所见即所得预览。
- 皮肤模板和品牌化玩家入口生成。

不复刻：

- Delphi 对象流和 VCL/DevExpress 控件。
- EXE 尾部自定义结构。
- 自定义编码、VMProtect 绑定和不透明二进制格式。
- 旧皮肤与旧启动器文件兼容。

### 2.2 `188BLUEM2` 微端资源服务

参考文件：

- `D:\ChuanQi\工具端\引擎\yuanma\188BLUEM2\Source\MicroResServer\MircoResourceServer.dpr`
- `D:\ChuanQi\工具端\引擎\yuanma\188BLUEM2\Source\MicroResServer\SResourceData.pas`

应借鉴的是独立运营入口和资源可观察性，不复刻旧协议。当前 `MicroGateway.Core`、`BootstrapPackage*`、`PcBootstrap*` 已覆盖并超过其运行能力。

### 2.3 `91/JONE` 游戏自定义 UI

参考文件：

- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\Common\uUITypes.pas`
- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\M2ProjectBuilder\M2UIDsgn.pas`
- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\Client\uCliUITypes.pas`
- `D:\ChuanQi\工具端\引擎\yuanma\91\JONE\trunk\Source\SceneUISSC\DWinCtl.pas`

应复刻的功能：

- 自定义窗口及子控件树。
- 图片、文本、按钮、输入、物品槽等常用控件。
- 画布预览、属性编辑、复制粘贴。
- 按钮命令、窗口切换、输入和物品参数提交。
- UI 随项目版本保存、发布并由客户端动态实例化。

不复刻：

- `DWinCtl` 控件实现。
- PaxCompiler 或任意客户端脚本执行。
- 任意字符串命令直达服务端。
- 旧 UI 项目文件和 NativeXml 对象存储。

---

## 3. 当前可复用基础

### 3.1 启动器

直接复用：

- `Launcher.Editor/EditorProject.cs`
- `Launcher.Editor/MainForm.cs`
- `Launcher.Editor/PlayerArtifactBuilder.cs`
- `Launcher.Editor/ProjectReleasePublisher.cs`
- `Launcher.Editor/EditorPreflightValidator.cs`
- `Launcher.ThemeRuntime/`
- `Launcher.PlayerShell/`
- `Launcher.PlayerShell.Core/`

这些模块已经负责项目、主题运行、玩家入口生成、签名发布、版本历史、离线包和回滚。新工作先修复真实玩家入口，再深化编辑体验，不另建启动器生成器或发布器。

### 3.2 微端

直接复用：

- `MicroGateway.Core/`
- `MicroGateway.App/`
- `Client_MonoGame.Shared/BootstrapPackageRuntime.cs`
- PC 端现有 `PcBootstrap*`
- `Shared.Security.BootstrapManifest*`

新工作只增加微端入口引用、资源预检、状态展示和内容发布编排，不重写缓存、资源读取、签名或下载协议。

微端生命周期必须与启动器生成彻底解耦：

| 对象 | 何时生成或发布 | 启动器生成时的处理 |
|---|---|---|
| `MicroGateway` 程序 | 微端代码、协议或部署配置发生兼容性变更时 | 不生成、不复制、不部署 |
| 微端资源索引与资源包 | 游戏内容版本发布时 | 仅引用已发布版本并做连通性、签名和兼容性预检 |
| 玩家启动器入口 | 启动器运行壳、内置主题、信任根或启动协议变更时 | 按需生成；不得携带微端服务程序 |
| 远程主题、公告与区服清单 | 运营配置变更时 | 独立签名发布，无需重生启动器 |
| 完整游戏客户端资源 | 客户端内容版本发布时 | 独立维护；启动器只选择和启动对应资源目录 |

因此，“管理工具中的微端管理”是管理端点、凭据、资源版本和诊断，不是每次为启动器编译一份微端程序。

### 3.3 游戏 GUI

直接复用：

- `Client_MonoGame.Shared/UI/FairyGui/Runtime/`
- `Client_MonoGame.Shared/UI/FairyGui/FairyGuiHost*.cs`
- PC 端 `Client_VorticeDX11/MirGraphics/DXManager.cs`
- 移动端 `Client_MonoGame.Shared/MirGraphics/SpriteBatchStack.cs`
- `Shared/Packet.cs` 及现有协议事实源
- `Server/Scripting/` 受控脚本 Hook

FairyGUI 运行时只作为移动端 Adapter 的实现基础。它目前不等于自定义 GUI 系统，不能把现有固定窗口接线直接当作设计器模型。

---

## 4. 目标架构

### 4.1 总体结构

```text
版本作者工作台
├─ 启动器设计器
├─ 微端与发行体
├─ 游戏 GUI 设计器
├─ 资源浏览器
├─ 项目预检
└─ 版本与发布
        │
        ▼
可视化设计核心 Module
├─ 文档对象树
├─ 选择/移动/缩放
├─ 对齐/吸附/分布
├─ 属性编辑
├─ 撤销/重做
├─ 模板与预制件
├─ 资源引用
├─ 诊断
└─ 预览编排
        │
        ├─ 启动器 Adapter → LauncherTheme / LauncherSnapshot
        ├─ PC 游戏 GUI Adapter → DXManager 接缝
        └─ 移动 GUI Adapter → FairyGUI / SpriteBatchStack 接缝
```

### 4.2 可视化设计核心 Module

该 Module 应为深 Module：画布命中、拖拽、选择、多选、吸附、对齐、撤销、属性修改、诊断和文档变更全部隐藏在实现内。

建议外部 Interface 保持很小：

```csharp
public interface IVisualDesignSession
{
    DesignSnapshot Snapshot { get; }
    DesignResult Apply(DesignCommand command);
    DesignValidationResult Validate(DesignTarget target);
    DesignPreview BuildPreview(DesignTarget target, DesignViewport viewport);
    DesignDocument Save();
}
```

说明：

- `DesignCommand` 统一表达新增、删除、移动、缩放、改属性、排序和撤销/重做。
- 调用方不直接操作内部可变控件树。
- Interface 返回结果和诊断，不直接弹窗或写文件。
- 文件保存、图片解码、运行时预览均通过内部 Seam 注入。
- 至少存在启动器和游戏 GUI 两个 Adapter 后，目标差异 Seam 才成为真实 Seam；第一阶段不得提前抽象所有未来控件。

### 4.3 文档与运行时分离

必须区分三种数据：

1. **设计文档**：包含辅助线、锁定、分组、编辑器备注等作者信息。
2. **运行描述**：只包含客户端显示和交互所需数据。
3. **运行状态**：玩家数据、窗口实例、输入和物品槽状态，只存在于服务端权威状态或客户端会话中。

禁止把三者混成一个可任意反序列化的对象图。

---

## 5. 阶段路线与门禁

本路线使用新的任务编号：`UXL`（启动器体验）、`MICRO-UX`（微端体验）、`GUI`（游戏自定义 GUI）、`WB`（工作台）。阶段严格串行，阶段内部可按文件所有权拆分。

### 阶段 0：修复真实启动链路阻断

目标：在继续改善编辑器前，先保证已生成玩家入口能从选区界面可靠进入游戏。

- `LAUNCH-01`：把游戏程序目录与完整资源目录拆成两个明确参数。
- `LAUNCH-02`：玩家入口转移到本机托管目录后，仍保留最初启动器所在目录作为资源来源。
- `LAUNCH-03`：从内置客户端核心启动 `Client.exe`，但将工作目录设置为经过校验的完整资源目录。
- `LAUNCH-04`：覆盖“原目录只有玩家入口和完整资源、没有外置 `Client.exe`”的自动测试和真实冒烟。
- `LAUNCH-05`：合并或等价移植已经存在于 `codex/launcher-editor-simple-chinese` 的相关修复，禁止第三次另写一套平行方案。

`GATE-LAUNCH` 退出条件：

1. 真实生成玩家入口并放入完整客户端根目录。
2. 启动器完成自托管转移后，选择区服并点击开始游戏。
3. 游戏进程使用所选区服地址启动，且能读取原客户端根目录的 `Data` 与核心资源。
4. 关闭游戏后会话标记清理正常；再次启动不受残留状态阻断。
5. 自动测试和一次真实客户端冒烟均通过，并保留运行输出或截图工件。

在 `GATE-LAUNCH` 通过前，不开始启动器画布增强和游戏 GUI 开发。

### 阶段 A：启动器设计器最小闭环

目标：版本作者无需手填坐标即可完成一套启动器皮肤并生成真实玩家入口。

首批范围：

- `UXL-01`：现有启动器主题文档与画布坐标双向映射。
- `UXL-02`：控件树、单选、多选、移动、缩放、层级调整。
- `UXL-03`：吸附线、对齐、等距分布、锁定和隐藏。
- `UXL-04`：属性面板与资源选择器；按钮四态集中编辑。
- `UXL-05`：撤销/重做和未保存提示。
- `UXL-06`：100%、125%、150%、200% DPI 预览和问题定位。
- `UXL-07`：生成玩家入口并运行离线冒烟。

明确不做：

- 不重写 `PlayerArtifactBuilder`、`ProjectReleasePublisher`。
- 不先做通用插件系统。
- 不做游戏 GUI 控件。

`GATE-UXL` 退出条件：

1. 从空白项目或模板创建启动器。
2. 在画布完成背景、启动按钮、公告、区服列表的布局。
3. 全程无需手工编辑 JSON 或坐标文本。
4. 撤销/重做覆盖新增、删除、移动、缩放和属性修改。
5. 四种 DPI 预检无越界、点击区域错位或文字裁切。
6. 生成真实玩家入口，离线启动和选区流程通过。
7. 现有签名发布、历史比较和回滚测试保持通过。

用户可见工件：设计器截图、生成的测试玩家入口、四 DPI 对比截图、通过的测试输出。

### 阶段 B：微端入口与发行体工作流整合

目标：版本作者能从一个项目页面配置和验证各区服微端入口，并发布客户端资源版本；微端服务程序保持独立部署。

任务：

- `MICRO-UX-01`：发行体概览，显示客户端核心、资源包、资源版本和签名身份。
- `MICRO-UX-02`：默认微端入口和区服覆盖的可视化配置。
- `MICRO-UX-03`：主/备用入口连通性、资源版本、签名身份一致性预检。
- `MICRO-UX-04`：资源目录扫描结果、缺失项、重复项、预计核心体积和完整体积展示。
- `MICRO-UX-05`：生成测试资源发布，启动 PC 与 Android 读取同一签名资源版本；不生成微端服务程序。
- `MICRO-UX-06`：失败诊断直接定位到区服、入口或资源包。

明确不做：

- 不重写 `MicroGateway.Core`。
- 不在生成启动器时编译、复制、打包或部署 `MicroGateway` 程序。
- 不改变微端签名格式。
- 不将微端入口与游戏登录地址混为一项。
- 不允许区服覆盖使用不同签名身份或不兼容资源版本。

`GATE-MICRO-UX` 退出条件：

1. 一个项目页面能解释完整发行体、核心交付、资源包、默认入口和区服覆盖之间的关系。
2. 错误配置在发布前失败，并给出可执行修复信息。
3. PC 与 Android 对同一发布版本完成首次获取、缓存命中和失败回退冒烟。
4. 现有签名、防降级、原子切换和回滚测试保持通过。

用户可见工件：发行体概览截图、预检错误截图、PC/Android 获取日志和缓存命中对比数据。

### 阶段 C：可视化设计核心深化

目标：把阶段 A 中已经证明有用的编辑行为收拢成可供第二个 Adapter 使用的深 Module。

任务：

- `GUI-CORE-01`：将设计命令、选择模型、几何变换、撤销和诊断从启动器窗体中抽出。
- `GUI-CORE-02`：建立稳定的设计文档版本和迁移规则，仅服务新 C# 格式。
- `GUI-CORE-03`：启动器 Adapter 接回后保持所有 `GATE-UXL` 行为不变。
- `GUI-CORE-04`：增加内存 Adapter，使 Interface 成为测试表面。

`GATE-GUI-CORE` 退出条件：

1. 启动器设计器行为无回归。
2. 核心 Module 不引用 WinForms、FairyGUI、MonoGame 或 Vortice。
3. 设计行为测试只通过 Interface 断言可观察结果。
4. 删除旧窗体内部重复编辑逻辑，不形成新旧两层并存。

用户可见工件：无回归的启动器设计器、通过的设计行为测试、可审查的代码 diff。

### 阶段 D：游戏 GUI 静态窗口 MVP

目标：版本作者可设计一个静态活动窗口，并在 PC 与 Android 使用同一运行描述显示。

第一版控件：

- `Window/Panel`
- `Image`
- `Text/RichText`
- `Button`
- `TextInput`
- `List`
- `ProgressBar`
- `ItemSlot`

第一版布局：

- 绝对位置与尺寸。
- 左、右、上、下和中心锚点。
- 横向/纵向排列容器。
- 边距与间距。
- PC 视口与 Android 安全区预览。

任务：

- `GUI-01`：游戏 GUI 设计文档和运行描述 Schema。
- `GUI-02`：设计器的游戏 GUI Adapter。
- `GUI-03`：PC 运行时 Adapter，只能通过 `DXManager` 绘制。
- `GUI-04`：移动运行时 Adapter，复用内嵌 FairyGUI 运行时和 `SpriteBatchStack`。
- `GUI-05`：资源引用、字体、图集和安全区校验。
- `GUI-06`：静态示例“新手活动窗口”双端显示。

`GATE-GUI-STATIC` 退出条件：

1. 同一设计文档生成同一版本的运行描述。
2. PC 与 Android 均显示标题、图片、列表、进度和按钮。
3. Android 无越过安全区，PC 无越界或模糊缩放。
4. 客户端只加载已签名、Schema 兼容的 GUI 包。
5. 未知控件、未知属性、循环引用和超限资源全部失败关闭。

用户可见工件：设计器截图、PC 截图、Android 截图、运行描述和校验测试输出。

### 阶段 E：服务端权威动态 GUI

目标：自定义 GUI 可以安全承载真实活动/NPC 业务，而不是只有静态展示。

动作白名单首版：

- `CloseWindow`
- `OpenWindow`
- `SwitchPage`
- `SubmitText`
- `SubmitSelection`
- `SubmitItems`
- `RequestAction`

数据绑定首版：

- 文本、布尔、整数、进度值。
- 有上限的列表。
- 有上限的物品槽集合。
- 按钮可见和可用状态。

任务：

- `GUI-07`：在 `Shared/` 定义真实协议包和严格上限。
- `GUI-08`：服务端窗口会话、版本绑定和重放保护。
- `GUI-09`：主线程验证动作、输入和物品所有权。
- `GUI-10`：PC/Android 状态投影和增量更新。
- `GUI-11`：脚本旁路 Hook，只允许声明窗口、提供数据和处理白名单动作。
- `GUI-12`：完成一个真实纵向闭环，例如“活动兑换窗口”。

安全规则：

- 客户端不提交价格、奖励结果或权威物品属性。
- 每个动作绑定窗口实例、GUI 版本和会话随机数。
- 服务端重新校验距离、NPC、活动状态、货币、物品和次数。
- 禁止任意脚本、反射调用、文件路径、URL 和未登记协议名。
- 玩家状态只在服务端主线程修改。

`GATE-GUI-DYNAMIC` 退出条件：

1. 服务端打开窗口、下发数据、接收动作、更新状态并关闭窗口全链路通过。
2. PC 和 Android 使用相同真实协议类型。
3. 篡改价格、物品、数量、动作、窗口版本和会话随机数均被拒绝。
4. 断线、重连、窗口过期和 GUI 版本切换行为明确。
5. 一个真实活动窗口完成 PC 与 Android 操作验收。

用户可见工件：真实业务录屏或截图、协议测试、安全失败日志和双端验收记录。

### 阶段 F：统一工作台与内容版本

目标：把启动器、发行体、GUI、资源和现有内容编辑入口组织成一个项目视图。

任务：

- `WB-01`：项目首页显示启动器版本、客户端发行体、GUI 包、服务端程序和 Schema 版本。
- `WB-02`：统一资源浏览器与反向引用查询。
- `WB-03`：统一预检，错误可以跳转到具体文档和控件。
- `WB-04`：生成可审阅的版本差异。
- `WB-05`：测试环境发布、健康检查和回滚编排。

工作台只负责编排，既有 Module 仍各自负责事实：

- 启动器发布事实归 `ProjectReleasePublisher`。
- 微端资源事实归 `MicroGateway/Bootstrap`。
- 协议事实归 `Shared/`。
- 数据库版本事实归 `SchemaMigrator`。
- 脚本运行事实归 `Server/Scripting`。

`GATE-WB` 退出条件：版本作者能够在一个项目中完成“编辑启动器 → 配置发行体 → 设计活动 GUI → 预检 → 发布测试版本 → 验证 → 回滚”，但数据仍分域存储，不形成巨型项目文件。

---

## 6. 第一批应立即实施的工作

第一批只完成阶段 0，不同时开工画布和游戏 GUI。当前工作不是继续堆启动器编辑功能，而是让用户已经生成的启动器真正能进入游戏。

### 任务 0：收敛并验收已有启动修复

工件：

- 当前分支与 `codex/launcher-editor-simple-chinese` 中启动修复的分支差异证据。
- 游戏程序目录与资源目录分离的最小代码 diff。
- “原客户端目录无外置 `Client.exe`”场景的自动测试输出。
- 真实生成玩家入口、点击开始游戏、进入角色或登录流程的截图或运行日志。

说明：已有修复不得停留在其他分支或测试桩里；只有合入当前交付线并完成真实冒烟，才算修复。

通过 `GATE-LAUNCH` 后，再启动阶段 A。阶段 A 建议拆成以下四个可独立验收的任务：

### 任务 1：启动器设计器现状基线

工件：

- 启动器编辑器当前页面截图。
- 从新建项目到生成玩家入口的实际操作录屏或逐步截图。
- 完成同一皮肤所需点击次数、手填字段数、失败点和总耗时。
- 100%、125%、150%、200% DPI 当前预览截图。

说明：这是阶段 A 唯一允许的体验基线，不建设分析工具。

### 任务 2：画布选择、移动和缩放

工件：

- 直接编辑现有 `LauncherTheme.Controls` 的真实画布。
- 单选和拖动。
- 八向缩放手柄。
- 控件树与画布选择同步。
- 保存、重新打开后位置和尺寸一致。
- 行为自动测试。

### 任务 3：属性与资源闭环

工件：

- 属性面板编辑位置、尺寸、可见、文字、颜色和图片。
- 资源选择器显示缩略图和引用位置。
- 启动按钮四态集中预览。
- 缺失资源错误可点击定位。

### 任务 4：撤销、DPI 与真实生成验收

工件：

- 撤销/重做。
- 对齐、吸附和锁定。
- 四 DPI 预览。
- 生成真实玩家入口。
- 离线选区启动截图与现有发布测试输出。

完成这四个任务并通过 `GATE-UXL` 后，再根据真实编辑器实现抽取可视化设计核心；禁止在任务 2 前先创建大型通用画布框架。

---

## 7. 建议文件落点

文件名可以在实施时调整，但职责必须保持清晰。

```text
Launcher.Editor/
├─ Design/
│  ├─ LauncherDesignSession.cs
│  ├─ LauncherCanvasView.cs
│  ├─ LauncherHierarchyView.cs
│  ├─ LauncherPropertyView.cs
│  └─ LauncherAssetBrowser.cs
└─ 继续复用现有生成与发布文件

Components/VisualDesign/                 # 阶段 C 才创建
├─ DesignDocument.cs
├─ DesignCommand.cs
├─ DesignSession.cs
├─ DesignGeometry.cs
├─ DesignHistory.cs
└─ DesignValidation.cs

Shared/CustomGui/                        # 阶段 D/E
├─ CustomGuiSchema.cs
├─ CustomGuiRuntimeDocument.cs
├─ CustomGuiPackets.cs 或纳入现有包文件
└─ CustomGuiLimits.cs

Client_VorticeDX11/CustomGui/            # PC Adapter
Client_MonoGame.Shared/UI/CustomGui/     # 移动 Adapter；复用 FairyGUI Runtime
Server/CustomGui/                        # 会话、动作验证、状态投影
Server.MirForms/CustomGui/               # 游戏 GUI 设计入口
```

不得创建：

- 第二套 `DXManager`。
- 第二套 `SpriteBatchStack`。
- 第二套 FairyGUI 运行时。
- 新协议序列化框架。
- 新数据库迁移框架。
- 新启动器生成器或新微端服务器。

---

## 8. 测试与验证策略

### 8.1 快内环

每次修改运行：

- 设计命令和撤销行为测试。
- 文档序列化 round-trip。
- 目标 Adapter 映射测试。
- 资源路径和尺寸校验。
- 对应项目局部构建。

### 8.2 视觉验证

每个设计器任务必须产出截图：

- 编辑状态。
- 保存后重新打开。
- 运行时实际画面。
- 不同 DPI 或设备视口。

预览截图不能替代真实生成物或客户端画面。

### 8.3 慢外环

只在阶段门禁运行：

- 真实玩家入口生成和离线启动。
- 启动器签名发布与回滚。
- PC/Android 微端首次下载和恢复。
- PC/Android 自定义 GUI 实际运行。
- 完整发布包和设备验收。

### 8.4 性能基线

启动器设计器：

- 500 个设计对象仍能流畅选择和拖动。
- 撤销历史有明确数量或内存上限。
- 缩略图后台加载，不阻塞 UI 线程。

游戏 GUI 运行时：

- 运行描述只在打开或版本变化时解析。
- 每帧不解析 JSON、不使用反射表达式绑定。
- 列表和物品槽有明确数量上限。
- PC 与 Android 分别记录构建窗口耗时、峰值分配和帧时间变化。

---

## 9. 主要风险与控制

| 风险 | 控制 |
|---|---|
| 先造通用设计器，长期没有可用工件 | 先在启动器真实场景完成阶段 A，阶段 C 才抽核心 |
| 启动器和游戏 UI 被强行统一成同一控件模型 | 共享编辑行为，运行文档和 Adapter 分离 |
| 游戏 GUI 变成客户端脚本系统 | 只允许声明式数据和白名单动作 |
| 服务端相信客户端提交数据 | 服务端主线程重新验证全部权威条件 |
| 绕过现有渲染接缝 | PC 只走 `DXManager`，移动只走 FairyGUI/`SpriteBatchStack` |
| 重写微端或发布系统 | 工作台只编排现有 Module |
| 巨型项目文件造成冲突和不可审阅 | 分域文档、稳定标识、文本化格式、版本迁移 |
| 设计器预览与运行时不一致 | Adapter 使用同一运行描述，并保留实际客户端截图门禁 |
| 功能范围无限增长 | 每阶段只实现门禁列出的控件和动作 |
| 参考源码许可证不清晰 | 只复刻功能和工作流，不复制实现代码和资源 |

---

## 10. 明确不做

本路线不包含：

- 兼容 `91/JONE` 项目、UI、皮肤或登录器文件。
- Delphi 到 C# 的逐行翻译。
- 复刻旧工具外观和按钮位置。
- 允许客户端动态执行 C#、JavaScript、Pascal 或任意表达式。
- 将玩家数据库或服务器运行目录打进普通内容版本。
- 用统一工作台替代 Git、SchemaMigrator、签名发布或备份系统。
- 每次生成启动器时重新生成、复制或部署微端服务程序。
- 在静态 GUI 门禁通过前实现复杂商城、交易或装备强化业务。

---

## 11. 阶段完成定义汇总

| 门禁 | 用户可以直接使用的结果 |
|---|---|
| `GATE-LAUNCH` | 生成的玩家入口选区后能可靠进入真实游戏客户端 |
| `GATE-UXL` | 用画布完成启动器皮肤并生成真实玩家入口 |
| `GATE-MICRO-UX` | 在同一项目内正确配置并验证发行体和微端入口 |
| `GATE-GUI-CORE` | 启动器编辑行为由稳定设计核心承载且无回归 |
| `GATE-GUI-STATIC` | 同一自定义窗口在 PC 与 Android 正确显示 |
| `GATE-GUI-DYNAMIC` | 一个真实活动窗口完成服务端权威双端闭环 |
| `GATE-WB` | 从项目编辑到测试发布和回滚形成统一作者工作流 |

---

## 12. 开工决策

下一项开发必须先完成 `LAUNCH-01..05`：将已有启动链路修复收敛到当前交付分支，并用真实生成的玩家入口完成“选区 → 开始游戏 → 进入客户端”冒烟。

通过 `GATE-LAUNCH` 后再开始 `UXL-01/02`，在现有 `Launcher.Editor` 中完成启动器画布选择、移动、缩放和保存闭环。

不先创建游戏 GUI 项目，不先修改协议，不先抽象通用控件库，也不把微端程序绑进启动器生成。只有真实启动链路和启动器画布编辑流程依次通过后，才能以实际代码为依据推进 `GUI-CORE`。

这条路线能最早改善当前最明显的使用问题，同时为后续游戏自定义 GUI 提供经过实际验证的设计基础。
