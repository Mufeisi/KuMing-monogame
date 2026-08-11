# GATE-L3 GM 启动器编辑器验收证据

## 可运行工件

- GM 编辑器：`artifacts/gate-l3-editor-20260811/editor-isolated-v5/GM启动器编辑器.exe`
  - 单目录仅 1 个 EXE；225,380,425 字节（214.94 MiB）。
  - SHA-256：`230C0F573EEC82CF10056F343137A6AD641AF6D5C88B10282A1603EBB8E20D0F`。
- 编辑器生成的玩家入口：`artifacts/gate-l3-editor-20260811/smoke-output-v5/smoke-project-玩家入口.exe`
  - 59,855,667 字节（57.08 MiB），低于 80 MiB 硬上限。
  - SHA-256：`6B8550094B50B40D2F2AC20E55026D98AA6440EB5F9D64AA5AB8148EC9448CE1`。
- 编辑器生成的独立微端包：`artifacts/gate-l3-editor-20260811/smoke-output-v5/smoke-project-微端网关.zip`
  - 50,234,913 字节（47.91 MiB）。
  - SHA-256：`B7CF5CA76918F08F99D1A4D1D6E620C413D25EFEC68E1B8F2E3D0C1122BC5B84`。

## 离线与发布验证

- 将 `PATH` 限制为 Windows 系统目录，并把 `DOTNET_ROOT` 指向不存在目录后，正式单文件编辑器执行 `--editor-smoke`，退出码为 0。
- 编辑器在该环境中完成项目新建、保存、真实预览、玩家 EXE 生成、微端 ZIP 生成和编辑器界面截图；GM 运行时不依赖源码、Visual Studio、SDK 或另装 .NET Desktop Runtime。
- 玩家入口复制并重命名为 `任意重命名传奇入口.exe` 后，`--shell-smoke` 退出码为 0；目录仅有该 EXE。
- 玩家入口的 `--theme-render-smoke` 等待真实载荷进程结束，退出码为 0，生成三模板四档 DPI 共 12 张可解码 PNG。
- 微端包解压后执行 `--gateway-smoke`，真实完成健康检查、鉴权和 `Range` 读取，退出码为 0；加密的 `gateway-secret.import` 成功导入 Windows 凭据管理器后已自动删除。
- `gateway-project.json` 只包含项目标识、自动生成的 User、监听地址、端口和目录提示，不包含明文 Code。

## 功能边界

- 编辑器支持断网多项目、可跳过向导、只读导入、三模板、下拉/侧栏区服模式、七种 GM 运营状态、公告、设置注册表、固定控件位置/尺寸/显隐/颜色/字体/图片/透明度和实时预览。
- PNG、JPG 直接导入；BMP 默认无损转为 PNG。按钮可只导入基础图，由运行时派生悬停、按下、禁用状态，也可逐态覆盖。
- 发布前强制执行四档 DPI 布局、点击命中、文字截断、控件重叠、素材完整性和链接合法性检查；坏主题不能生成玩家 EXE 或网关包。
- 每个新项目自动生成独立微端 User，并将同一 User/Code 同步到玩家入口和网关包；Code 不写普通 INI、JSON 或命令行。

## 自动测试

- 玩家入口、主题与编辑器集成：56/56，通过，见 `gate-l3-editor.trx`。
- 微端核心与宿主专项：4/4，通过，见 `gate-l3-gateway.trx`。
- 两轴复审：Standards 与 Spec 均无剩余硬问题。

## 截图

- `screenshots/editor-ui.png`：正式编辑器真实 WinForms 界面。
- `screenshots/editor-preview.png`：编辑器使用共享渲染模块生成的实时预览。
- `screenshots/generated-player-widescreen.png`：编辑器生成的玩家 EXE 实际渲染截图。

物理 Windows 100%、125%、150%、200% 多屏演练与第二台无 SDK 机器复验，继续作为 GATE-L5 的外部交付演练项，不在本机伪造。

## 语言

证据说明使用中文；命令、类型名和原始参数保留英文。
