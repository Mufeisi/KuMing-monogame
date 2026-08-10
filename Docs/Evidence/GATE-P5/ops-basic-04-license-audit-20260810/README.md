# OPS-BASIC-04 授权与依赖审计证据

## 工件

- `ops-basic-04-targeted.trx`：合规专项 2/2，通过。
- `base05-full.trx`：Base05 全量 343/343，通过。
- `server-library-build.log`：`Server.Library` Release，0 错误。
- `server-host-build.log`：Windows 服务端 Release，0 错误。
- `pc-build.log`：PC 客户端 Release，0 错误。
- `android-build.log`：Android arm64 Release，0 错误。
- `vulnerability-scan.txt`：Windows、Android、iOS 直接与传递依赖漏洞复扫均为 0。
- `sbom-generation.txt`：Microsoft SBOM Tool 4.1.5 标准 SPDX 生成结果。
- `release-notice-proof.txt`：PC、服务端、Android APK 与 iOS 项目均携带第三方声明。
- `resource-inventory.txt`：授权资源目录分类、文件数和大小实测。

## 关键结论

初次漏洞扫描发现 `log4net 3.0.3` 中危和 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 高危；升级到 3.3.2 与 2.1.12 后，三个发布集合复扫均无报告项。SBOM 覆盖 6 个真实 Release 工件，含 218 个包、6 个文件、988 条关系；自动取得 204/221 个唯一组件许可证，剩余 12 个 `NOASSERTION` 项均在随包声明中人工复核，专项测试会阻止出现未列入复核表的新项。

外部资源未复制进 Git。`D:\ChuanQi\Crystal_monogame` 的服务器、客户端和资源授权依据为项目所有者的明确确认；清单按客户端数据、地图、音频、微端、FairyGUI、字体和启动/着色器资源分类。

## 执行说明

Microsoft SBOM Tool 的 `BuildListFile` 首次把 LF 逐行列表误解析为单一路径；未生成可用最终工件。随后改为把六个精确 Release 文件复制到独立临时发布目录，未创建仓库内生成器。许可证在线补充有一次 30 秒超时，改为仓库外输出并将超时提高到 120 秒后成功取得 204 项许可证信息。最终又将组件目录收窄为生产项目的 `csproj + project.assets.json`，排除 Tests、Docs 和旧 SBOM，避免把测试框架或清单自身算入产品。

四个目标构建顺序执行，避免共享输出争用。Android AOT 构建用时约 2 分 24 秒，仍低于单次 30 分钟慢外环阈值。`git diff --check` 在提交前复核。

## 每日工件检查

- 工件：依赖升级代码、标准 SBOM、授权 JSON、随包第三方声明、专项/全量 TRX、四份构建日志及扫描证据。
- 过程资产：任务简报与本说明；数量少于可运行/可审计工件。
- 语言：交流、文档、证据说明与提交信息使用中文；命令、代码标识符、许可证原文和工具原始输出保持原文。
