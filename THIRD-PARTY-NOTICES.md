# 第三方组件与资源声明

本文件随 PC、Android、iOS 客户端和 Windows 服务端发布。每个发布物的 `Compliance` 目录同时包含 `SBOM/dependencies.spdx.json`、`external-assets.manifest.json` 和 `Licenses` 许可证正文；接收者无需仓库即可读取这些材料。最终二进制文件摘要另见发布侧车 `Docs/Compliance/SBOM/manifest.spdx.json`，该侧车不嵌入自身所校验的 APK，以避免自引用哈希。

## SBOM 许可证复核

微软 SBOM Tool 4.1.5 共识别 218 个包：MIT 99 个、MIT 与 Apache-2.0 双许可 89 个、Apache-2.0 7 个、MIT/BSD-2-Clause/Apache-2.0 组合 4 个、Zlib 3 个、MS-PL 2 个、EPL-2.0 1 个、MPL-2.0 1 个。其余 12 个 `NOASSERTION` 项已人工复核如下；其中一个是本产品自身。

| 组件 | 版本 | 人工复核结果 |
| --- | --- | --- |
| LyoCrystal | 2026.08.10 | 本产品自身，不作为第三方包授权 |
| StbTrueTypeSharp | 1.26.11 | 上游 `rds1983/StbSharp` README 声明 Public Domain |
| StbImageSharp | 2.27.13 | 上游 README 声明 Public Domain or MIT |
| System.Configuration.ConfigurationManager / System.Security.Permissions / System.Data.DataSetExtensions | 4.5.0 | NuGet 元数据指向 .NET CoreFX MIT 许可证 |
| Microsoft.Windows.SDK.Win32Metadata / Win32Docs / WDK.Win32Metadata | 见 SBOM | NuGet 包内 `sdk_license.txt` 或 Microsoft Windows SDK 许可证链接 |
| Microsoft.Web.WebView2 | 1.0.2903.40 | NuGet 包内 `LICENSE.txt` 与 `NOTICE.txt` |
| Microsoft.AspNet.WebApi.Client | 6.0.0 | NuGet 包内 `.NET Library EULA` |
| NAudio | 2.2.1 | NuGet 包内 `license.txt`，MIT |

上述包的精确 PURL、版本与依赖关系以随包依赖 SPDX 为准；最终发布文件哈希以发布侧车 SPDX 为准。许可证、NOTICE 或 EULA 正文均收录在随包 `Compliance/Licenses`，发布时不得删除。

## FairyGUI MonoGame

来源：https://github.com/fairygui/FairyGUI-monogame

MIT License

Copyright (c) 2018 FairyGUI

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## HarmonyOS Sans SC Medium

内嵌字体名称：HarmonyOS Sans SC Medium；字体元数据声明：`Copyright 2021 Huawei Device Co., Ltd. All Rights Reserved.`。本项目使用和发布该字体及游戏素材的依据，是项目所有者于 2026-08-10 对 `D:\ChuanQi\Crystal_monogame` 全部服务器、客户端与资源作出的明确授权确认。该确认只覆盖本项目，不得被解释为向其他项目转授华为商标或字体权利。
