# OPS-BASIC-04 授权与依赖审计

## 交付范围

- Windows 发布集合、Android 与 iOS 的 NuGet 直接/传递依赖漏洞扫描。
- PC、Android、服务端真实 Release 工件的 SPDX 2.2 SBOM。
- NuGet 许可证分类、`NOASSERTION` 人工复核和随包第三方声明。
- 外部素材、字体、FairyGUI、音频、地图及微端资源授权清单。

## 漏洞处置

初次扫描发现 `log4net 3.0.3` 的中危通告 `GHSA-4f7c-pmjv-c25w`，以及 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的高危通告 `GHSA-2m69-gcr7-jv3q`。服务端直接依赖已升级为 `log4net 3.3.2`，并以 `SQLitePCLRaw.lib.e_sqlite3 2.1.12` 约束上层 `Microsoft.Data.Sqlite` 的传递解析结果。升级后 Windows、Android、iOS 三组 `dotnet list package --vulnerable --include-transitive` 均未报告漏洞，目标构建和 Base05 回归必须继续通过。

## SBOM 与许可证

`Docs/Compliance/SBOM/manifest.spdx.json` 由 Microsoft SBOM Tool 4.1.5 生成，覆盖服务端、PC 客户端和 Android 签名 APK 共六个 Release 工件；包含 218 个包、6 个文件及 988 条关系。在线许可证补充识别 204/221 个唯一组件许可证；SPDX 内 12 个 `NOASSERTION` 项由 `THIRD-PARTY-NOTICES.md` 逐项复核，不允许留下未列入复核表的新项。

`THIRD-PARTY-NOTICES.md` 由 PC、Android、iOS 与服务端项目复制或打包进发布物。以后新增、删除或升级依赖时，必须重新生成 SBOM、重跑漏洞扫描并更新人工复核表。

## 外部资源授权

`Docs/Compliance/external-assets.manifest.json` 记录资源来源、类别、数量、大小、用途和授权依据。项目所有者已明确确认 `D:\ChuanQi\Crystal_monogame` 内服务器、客户端及资源全部授权给本项目；本清单将该确认固化为可审计记录，不把确认扩大为对第三方商标或字体权利的转授权。

资源发布仍必须经过 SEC-06 已有签名清单；本任务不修改资源内容、不复制 11GB 资源进 Git，也不重造微端或发布流水线。
